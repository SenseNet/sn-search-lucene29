using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Schema;
using SenseNet.ContentRepository.Search;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.ContentRepository.Storage.Data.MsSqlClient;
using SenseNet.ContentRepository.Storage.Schema;
using SenseNet.Diagnostics;
using SenseNet.Search;
using SenseNet.Search.Lucene29;
using SenseNet.Search.Querying;

namespace IndexIntegrityChecker
{
    public enum IndexDifferenceKind { NotInIndex, NotInDatabase, MoreDocument, DifferentNodeTimestamp, DifferentVersionTimestamp, DifferentLastPublicFlag, DifferentLastDraftFlag }

    [DebuggerDisplay("{Kind} VersionId: {VersionId}, DocId: {DocId}")]
    [Serializable]
    public class Difference
    {
        public Difference(IndexDifferenceKind kind)
        {
            this.Kind = kind;
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public IndexDifferenceKind Kind { get; internal set; }
        /// <summary>
        /// Not used used if the Kind is NotInIndex
        /// </summary>
        public int DocId { get; internal set; }
        public int VersionId { get; internal set; }
        public long DbNodeTimestamp { get; internal set; }
        public long DbVersionTimestamp { get; internal set; }
        /// <summary>
        /// Not used used if the Kind is NotInIndex
        /// </summary>
        public long IxNodeTimestamp { get; internal set; }
        /// <summary>
        /// Not used used if the Kind is NotInIndex
        /// </summary>
        public long IxVersionTimestamp { get; internal set; }
        /// <summary>
        /// Not used used if the Kind is NotInIndex
        /// </summary>
        public int NodeId { get; internal set; }
        /// <summary>
        /// Not used used if the Kind is NotInIndex
        /// </summary>
        public string Path { get; internal set; }
        /// <summary>
        /// Not used used if the Kind is NotInIndex
        /// </summary>
        public string Version { get; internal set; }

        /// <summary>
        /// Not used used if the Kind is other than DifferentVersionFlag
        /// </summary>
        public bool IsLastPublic { get; internal set; }
        /// <summary>
        /// Not used used if the Kind is other than DifferentVersionFlag
        /// </summary>
        public bool IsLastDraft { get; internal set; }

        public override string ToString()
        {
            if (this.Kind == IndexDifferenceKind.NotInIndex)
            {
                var head = NodeHead.GetByVersionId(this.VersionId);
                if (head != null)
                {
                    var versionNumber = head.Versions
                        .Where(x => x.VersionId == this.VersionId)
                        .Select(x => x.VersionNumber)
                        .FirstOrDefault()
                        ?? new VersionNumber(0, 0);
                    this.Path = head.Path;
                    this.NodeId = head.Id;
                    this.Version = versionNumber.ToString();
                }
            }

            var sb = new StringBuilder();
            sb.Append(Kind).Append(": ");
            if (DocId >= 0)
                sb.Append("DocId: ").Append(DocId).Append(", ");
            if (VersionId > 0)
                sb.Append("VersionId: ").Append(VersionId).Append(", ");
            if (NodeId > 0)
                sb.Append("NodeId: ").Append(NodeId).Append(", ");
            if (Version != null)
                sb.Append("Version: ").Append(Version).Append(", ");
            if (DbNodeTimestamp > 0)
                sb.Append("DbNodeTimestamp: ").Append(DbNodeTimestamp).Append(", ");
            if (IxNodeTimestamp > 0)
                sb.Append("IxNodeTimestamp: ").Append(IxNodeTimestamp).Append(", ");
            if (DbVersionTimestamp > 0)
                sb.Append("DbVersionTimestamp: ").Append(DbVersionTimestamp).Append(", ");
            if (IxVersionTimestamp > 0)
                sb.Append("IxVersionTimestamp: ").Append(IxVersionTimestamp).Append(", ");
            if (Path != null)
                sb.Append("Path: ").Append(Path);
            return sb.ToString();
        }
    }

    internal class TimestampData
    {
        public int NodeId { get; set; }
        public int VersionId { get; set; }
        public long NodeTimestamp { get; set; }
        public long VersionTimestamp { get; set; }
        public int LastMajorVersionId { get; set; }
        public int LastMinorVersionId { get; set; }
    }

    public class IndexIntegrityChecker
    {
        // ReSharper disable once InconsistentNaming
        private static LuceneSearchManager __luceneSearchManager;
        private static LuceneSearchManager LuceneSearchManager
        {
            get
            {
                if (__luceneSearchManager == null)
                {
                    var engine = (ILuceneIndexingEngine)Providers.Instance.SearchEngine.IndexingEngine;
                    __luceneSearchManager = engine.LuceneSearchManager;
                }
                return __luceneSearchManager;
            }
        }

        //[SenseNet.ApplicationModel.ODataFunction]
        //public static object CheckIndexIntegrity(SenseNet.ContentRepository.Content content, bool recurse)
        //{
        //    var path = content == null ? null : content.Path;

        //    var completionState = LuceneSearchManager.ReadActivityStatusFromIndex();
        //    var lastDatabaseId = DataStore.GetLastIndexingActivityIdAsync(CancellationToken.None)
        //        .ConfigureAwait(false).GetAwaiter().GetResult();
        //    var channel = SenseNet.ContentRepository.DistributedApplication.ClusterChannel;
        //    var appDomainName = channel?.ReceiverName;

        //    return new
        //    {
        //        AppDomainName = appDomainName,
        //        LastStoredActivity = lastDatabaseId,
        //        LastProcessedActivity = completionState.LastActivityId,
        //        GapsLength = completionState.Gaps.Length,
        //        completionState.Gaps,
        //        Differences = Check(path, recurse)
        //    };
        //}

        public static IEnumerable<Difference> Check()
        {
            return Check(null, true);
        }
        public static IEnumerable<Difference> Check(string path, bool recurse)
        {
            if (recurse)
            {
                if (path != null)
                    if (string.Equals(path, Repository.RootPath, StringComparison.CurrentCultureIgnoreCase))
                        path = null;
                return new IndexIntegrityChecker().CheckRecurse(path);
            }
            return new IndexIntegrityChecker().CheckNode(path ?? Repository.RootPath);
        }

        /*==================================================================================== Instance part */

        private IEnumerable<Difference> CheckNode(string path)
        {
            var result = new List<Difference>();
            using (var readerFrame = LuceneSearchManager.GetIndexReaderFrame())
            {
                var ixReader = readerFrame.IndexReader;
                var docIds = new List<int>();
                var timestampData = GetTimestampDataForOneNodeIntegrityCheckAsync(path, GetExcludedNodeTypeIds())
                    .ConfigureAwait(false).GetAwaiter().GetResult();

                var dbDocId = CheckDbAndIndex(timestampData, ixReader, result);
                if (dbDocId >= 0)
                    docIds.Add(dbDocId);

                var scoreDocs = GetDocsUnderTree(path, false);
                foreach (var scoreDoc in scoreDocs)
                {
                    var docId = scoreDoc.Doc;
                    var doc = ixReader.Document(docId);
                    if (!docIds.Contains(docId))
                    {
                        result.Add(new Difference(IndexDifferenceKind.NotInDatabase)
                        {
                            DocId = scoreDoc.Doc,
                            VersionId = ParseInt(doc.Get(IndexFieldName.VersionId)),
                            NodeId = ParseInt(doc.Get(IndexFieldName.NodeId)),
                            Path = path,
                            Version = doc.Get(IndexFieldName.Version),
                            IxNodeTimestamp = ParseLong(doc.Get(IndexFieldName.NodeTimestamp)),
                            IxVersionTimestamp = ParseLong(doc.Get(IndexFieldName.VersionTimestamp))
                        });
                    }
                }
            }
            return result;
        }

        private int intSize = sizeof(int) * 8;
        private int _numDocs;
        private int[] _docBits;
        private IEnumerable<Difference> CheckRecurse(string path)
        {
            var result = new List<Difference>();

            using (var op = SnTrace.Index.StartOperation("Index Integrity Checker: CheckRecurse {0}", path))
            {
                using (var readerFrame = LuceneSearchManager.GetIndexReaderFrame())
                {
                    var ixReader = readerFrame.IndexReader;
                    _numDocs = ixReader.NumDocs() + ixReader.NumDeletedDocs();
                    var x = _numDocs / intSize;
                    var y = _numDocs % intSize;
                    _docBits = new int[x + (y > 0 ? 1 : 0)];
                    if (path == null)
                    {
                        if (y > 0)
                        {
                            var q = 0;
                            for (var i = 0; i < y; i++)
                                q += 1 << i;
                            _docBits[_docBits.Length - 1] = q ^ (-1);
                        }
                    }
                    else
                    {
                        for (var i = 0; i < _docBits.Length; i++)
                            _docBits[i] = -1;
                        var scoreDocs = GetDocsUnderTree(path, true);
                        for (var i = 0; i < scoreDocs.Length; i++)
                        {
                            var docId = scoreDocs[i].Doc;
                            _docBits[docId / intSize] ^= 1 << docId % intSize;
                        }
                    }


                    //var proc = DataStore.DataProvider.GetTimestampDataForRecursiveIntegrityCheck(path, GetExcludedNodeTypeIds());
                    var timestampData = GetTimestampDataForRecursiveIntegrityCheckAsync(path, GetExcludedNodeTypeIds())
                        .ConfigureAwait(false).GetAwaiter().GetResult();

                    foreach (var item in timestampData)
                    {
                        var docId = CheckDbAndIndex(item, ixReader, result);
                        if (docId > -1)
                            _docBits[docId / intSize] |= 1 << docId % intSize;
                    }


                    for (var i = 0; i < _docBits.Length; i++)
                    {
                        if (_docBits[i] != -1)
                        {
                            var bits = _docBits[i];
                            for (var j = 0; j < intSize; j++)
                            {
                                if ((bits & (1 << j)) == 0)
                                {
                                    var docId = i * intSize + j;
                                    if (docId >= _numDocs)
                                        break;
                                    if (!ixReader.IsDeleted(docId))
                                    {
                                        var doc = ixReader.Document(docId);
                                        if (!IsCommitDocument(doc))
                                        {
                                            result.Add(new Difference(IndexDifferenceKind.NotInDatabase)
                                            {
                                                DocId = docId,
                                                VersionId = ParseInt(doc.Get(IndexFieldName.VersionId)),
                                                NodeId = ParseInt(doc.Get(IndexFieldName.NodeId)),
                                                Path = doc.Get(IndexFieldName.Path),
                                                Version = doc.Get(IndexFieldName.Version),
                                                IxNodeTimestamp = ParseLong(doc.Get(IndexFieldName.NodeTimestamp)),
                                                IxVersionTimestamp = ParseLong(doc.Get(IndexFieldName.VersionTimestamp))
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                op.Successful = true;
            }
            return result.ToArray();
        }

        private bool IsCommitDocument(Document doc)
        {
            //doc.fields_ForNUnit
            //    Count = 2
            //        [0]: {stored / uncompressed,indexed <$#COMMIT:$#COMMIT>}
            //        [1]: {stored/uncompressed,indexed<$#DATA:a073dd12-5534-440b-b84a-b77e963065ef>}
            return doc.Get("$#COMMIT") == "$#COMMIT";
        }

        private int CheckDbAndIndex(TimestampData dbData, IndexReader ixReader, List<Difference> result)
        {
            var nodeIdFromDb = dbData.NodeId;
            var versionId = dbData.VersionId;
            var dbNodeTimestamp = dbData.NodeTimestamp;
            var dbVersionTimestamp = dbData.VersionTimestamp;
            var lastMajorVersionId = dbData.LastMajorVersionId;
            var lastMinorVersionId = dbData.LastMinorVersionId;
            var termDocs = ixReader.TermDocs(new Term(IndexFieldName.VersionId, NumericUtils.IntToPrefixCoded(versionId)));
            var docId = -1;
            if (termDocs.Next())
            {
                docId = termDocs.Doc();
                var doc = ixReader.Document(docId);
                var indexNodeTimestamp = ParseLong(doc.Get(IndexFieldName.NodeTimestamp));
                var indexVersionTimestamp = ParseLong(doc.Get(IndexFieldName.VersionTimestamp));
                var nodeId = ParseInt(doc.Get(IndexFieldName.NodeId));
                var version = doc.Get(IndexFieldName.Version);
                var p = doc.Get(IndexFieldName.Path);
                if (termDocs.Next())
                {
                    result.Add(new Difference(IndexDifferenceKind.MoreDocument)
                    {
                        DocId = docId,
                        NodeId = nodeId,
                        VersionId = versionId,
                        Version = version,
                        Path = p,
                        DbNodeTimestamp = dbNodeTimestamp,
                        DbVersionTimestamp = dbVersionTimestamp,
                        IxNodeTimestamp = indexNodeTimestamp,
                        IxVersionTimestamp = indexVersionTimestamp,
                    });
                }
                if (dbVersionTimestamp != indexVersionTimestamp)
                {
                    result.Add(new Difference(IndexDifferenceKind.DifferentVersionTimestamp)
                    {
                        DocId = docId,
                        VersionId = versionId,
                        DbNodeTimestamp = dbNodeTimestamp,
                        DbVersionTimestamp = dbVersionTimestamp,
                        IxNodeTimestamp = indexNodeTimestamp,
                        IxVersionTimestamp = indexVersionTimestamp,
                        NodeId = nodeId,
                        Version = version,
                        Path = p
                    });
                }

                // Check version flags by comparing them to the db: we assume that the last
                // major and minor version ids in the Nodes table is the correct one.
                var isLastPublic = doc.Get(IndexFieldName.IsLastPublic);
                var isLastDraft = doc.Get(IndexFieldName.IsLastDraft);
                var isLastPublicInDb = versionId == lastMajorVersionId;
                var isLastDraftInDb = versionId == lastMinorVersionId;
                var isLastPublicInIndex = isLastPublic == IndexValue.Yes;
                var isLastDraftInIndex = isLastDraft == IndexValue.Yes;

                if (isLastPublicInDb != isLastPublicInIndex)
                {
                    result.Add(new Difference(IndexDifferenceKind.DifferentLastPublicFlag)
                    {
                        DocId = docId,
                        VersionId = versionId,
                        DbNodeTimestamp = dbNodeTimestamp,
                        DbVersionTimestamp = dbVersionTimestamp,
                        IxNodeTimestamp = indexNodeTimestamp,
                        IxVersionTimestamp = indexVersionTimestamp,
                        NodeId = nodeId,
                        Version = version,
                        Path = p,
                        IsLastPublic = isLastPublicInIndex,
                        IsLastDraft = isLastDraftInIndex
                    });
                }
                if (isLastDraftInDb != isLastDraftInIndex)
                {
                    result.Add(new Difference(IndexDifferenceKind.DifferentLastDraftFlag)
                    {
                        DocId = docId,
                        VersionId = versionId,
                        DbNodeTimestamp = dbNodeTimestamp,
                        DbVersionTimestamp = dbVersionTimestamp,
                        IxNodeTimestamp = indexNodeTimestamp,
                        IxVersionTimestamp = indexVersionTimestamp,
                        NodeId = nodeId,
                        Version = version,
                        Path = p,
                        IsLastPublic = isLastPublicInIndex,
                        IsLastDraft = isLastDraftInIndex
                    });
                }

                if (dbNodeTimestamp != indexNodeTimestamp)
                {
                    var ok = false;
                    if (isLastDraft != IndexValue.Yes)
                    {
                        var latestDocs = ixReader.TermDocs(new Term(IndexFieldName.NodeId, NumericUtils.IntToPrefixCoded(nodeId)));
                        Document latestDoc = null;
                        while (latestDocs.Next())
                        {
                            var latestDocId = latestDocs.Doc();
                            var d = ixReader.Document(latestDocId);
                            if (d.Get(IndexFieldName.IsLastDraft) != IndexValue.Yes)
                                continue;
                            latestDoc = d;
                            break;
                        }
                        var latestPath = latestDoc?.Get(IndexFieldName.Path);
                        if (latestPath == p)
                            ok = true;
                    }
                    if (!ok)
                    {
                        result.Add(new Difference(IndexDifferenceKind.DifferentNodeTimestamp)
                        {
                            DocId = docId,
                            VersionId = versionId,
                            DbNodeTimestamp = dbNodeTimestamp,
                            DbVersionTimestamp = dbVersionTimestamp,
                            IxNodeTimestamp = indexNodeTimestamp,
                            IxVersionTimestamp = indexVersionTimestamp,
                            NodeId = nodeId,
                            Version = version,
                            Path = p
                        });
                    }
                }
            }
            else
            {
                result.Add(new Difference(IndexDifferenceKind.NotInIndex)
                {
                    DocId = docId,
                    VersionId = versionId,
                    DbNodeTimestamp = dbNodeTimestamp,
                    DbVersionTimestamp = dbVersionTimestamp,
                    NodeId = nodeIdFromDb
                });
            }
            return docId;
        }
        private ScoreDoc[] GetDocsUnderTree(string path, bool recurse)
        {
            var field = recurse ? "InTree" : "Path";

            var queryContext = SnQueryContext.CreateDefault();
            var snQuery = SnQuery.Parse($"{field}:'{path.ToLower()}'", null);

            var lq = Compile(snQuery, queryContext); //LucQuery.Parse(String.Format("{0}:'{1}'", field, path.ToLower()));

            using (var readerFrame = LuceneSearchManager.GetIndexReaderFrame())
            {
                var idxReader = readerFrame.IndexReader;
                var searcher = new IndexSearcher(idxReader);
                var numDocs = idxReader.NumDocs();
                try
                {
                    var collector = TopScoreDocCollector.Create(numDocs, false);
                    searcher.Search(lq.Query, collector);
                    var topDocs = collector.TopDocs(0, numDocs);
                    return topDocs.ScoreDocs;
                }
                finally
                {
                    searcher.Close();
                }
            }
        }

        private static int[] GetExcludedNodeTypeIds()
        {
            // We must exclude those content types from the integrity check
            // where indexing is completely switched OFF, because otherwise
            // these kinds of content would appear as missing items.
            return ContentType.GetContentTypes().Where(ct => !ct.IndexingEnabled)
                    .Select(ct => Providers.Instance.StorageSchema.NodeTypes[ct.Name].Id).ToArray();
        }

        private static int ParseInt(string data)
        {
            if (int.TryParse(data, out var result))
                return result;
            return -1;
        }
        private static long ParseLong(string data)
        {
            if (long.TryParse(data, out var result))
                return result;
            return -1;
        }


        private readonly JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            Formatting = Formatting.Indented
        };

        public void SaveCommitUserData(string savedIndexDir)
        {
            using (var readerFrame = LuceneSearchManager.GetIndexReaderFrame())
            {
                var ixReader = readerFrame.IndexReader;

                var commitPath = Path.Combine(savedIndexDir, "commitUserData.txt");
                using (var writer = new StreamWriter(commitPath, false))
                    JsonSerializer.Create(_jsonSerializerSettings).Serialize(writer, ixReader.GetCommitUserData());
            }
        }
        public void SaveRawIndex(string savedIndexDir)
        {
            var index = new Dictionary<string, Dictionary<string, string>>();
            using (var readerFrame = LuceneSearchManager.GetIndexReaderFrame())
            {
                var ixReader = readerFrame.IndexReader;
                var terms = ixReader.Terms();
                while (terms.Next())
                {
                    var term = terms.Term();
                    var field = term.Field();
                    var text = GetTermText(term);
                    if (text == null)
                        continue;

                    if (!index.TryGetValue(field, out var fieldValues))
                        index.Add(field, (fieldValues = new Dictionary<string, string>()));

                    var termDocs = ixReader.TermDocs(term);
                    var docs = new List<int>();
                    int doc;
                    while (termDocs.Next())
                        if (!ixReader.IsDeleted((doc = termDocs.Doc())))
                            docs.Add(doc);
                    fieldValues[text] = string.Join(",", docs.Select(x => x.ToString()));
                }
            }
            var indexPath = Path.Combine(savedIndexDir, "decryptedIndex.txt");


            using (var writer = new StreamWriter(indexPath, false))
                JsonSerializer.Create(_jsonSerializerSettings).Serialize(writer, index);

        }
        public void SaveIndexDocs(string savedIndexDir)
        {
            var documents = new Dictionary<int, Dictionary<string, string>>();
            using (var readerFrame = LuceneSearchManager.GetIndexReaderFrame())
            {
                var ixReader = readerFrame.IndexReader;
                var terms = ixReader.Terms();
                while (terms.Next())
                {
                    var term = terms.Term();
                    var field = term.Field();
                    var text = GetTermText(term);
                    if(text == null)
                        continue;

                    var termDocs = ixReader.TermDocs(term);
                    int doc;
                    while (termDocs.Next())
                        if (!ixReader.IsDeleted((doc = termDocs.Doc())))
                            AddFieldToDocument(documents, doc, field, text);
                }
            }
            var indexPath = Path.Combine(savedIndexDir, "decryptedDocuments.txt");

            using (var writer = new StreamWriter(indexPath, false))
                JsonSerializer.Create(_jsonSerializerSettings).Serialize(writer, documents);

        }
        private void AddFieldToDocument(Dictionary<int, Dictionary<string, string>> docs, int doc, string field, string text)
        {
            if (!docs.TryGetValue(doc, out var document))
                docs.Add(doc, document = new Dictionary<string, string>());
            if (!document.TryGetValue(field, out var value))
                document.Add(field, text);
            else
                document[field] = $"{value}, {text}";
        }

        private string GetTermText(Term term)
        {
            var fieldName = term.Field();
            var fieldText = term.Text();
            if (fieldText == null)
                return null;

            var fieldType = default(IndexValueType);

            if (!(LuceneSearchManager?.IndexFieldTypeInfo?.TryGetValue(fieldName, out fieldType) ?? false))
            {
                switch (fieldName)
                {
                    case "NodeTimestamp":
                    case "VersionTimestamp":
                        fieldType = IndexValueType.Long;
                        break;
                    default:
                        var c = fieldText.ToCharArray();
                        for (var i = 0; i < c.Length; i++)
                            if (c[i] < ' ')
                                c[i] = '.';
                        return new string(c);
                }
            }

            var pt = Providers.Instance.StorageSchema.PropertyTypes[fieldName];
            if (pt == null)
            {
                switch (fieldName)
                {
                    case "CreatedBy":
                    case "ModifiedBy":
                    case "Owner":
                    case "VersionCreatedBy":
                    case "VersionModifiedBy":
                    case "Workspace":
                        fieldType = IndexValueType.Int;
                        break;
                        //case "NodeTimestamp":
                        //case "VersionTimestamp":
                        //    fieldType = IndexValueType.Long;
                        //    break;
                }
            }
            else
            {
                if (pt.DataType == DataType.Reference)
                    fieldType = IndexValueType.Int;
            }

            string check;
            switch (fieldType)
            {
                case IndexValueType.Bool:
                case IndexValueType.String:
                case IndexValueType.StringArray:
                    return fieldText;
                case IndexValueType.Int:
                    var intValue = NumericUtils.PrefixCodedToInt(fieldText);
                    check = NumericUtils.IntToPrefixCoded(intValue);
                    if (check != fieldText)
                        return null;
                    return Convert.ToString(intValue, CultureInfo.InvariantCulture);
                case IndexValueType.Long:
                    var longValue = NumericUtils.PrefixCodedToLong(fieldText);
                    check = NumericUtils.LongToPrefixCoded(longValue);
                    if (check != fieldText)
                        return null;
                    return Convert.ToString(longValue, CultureInfo.InvariantCulture);
                case IndexValueType.Float:
                    var floatValue = NumericUtils.PrefixCodedToFloat(fieldText);
                    check = NumericUtils.FloatToPrefixCoded(floatValue);
                    if (check != fieldText)
                        return null;
                    return Convert.ToString(floatValue, CultureInfo.InvariantCulture);
                case IndexValueType.Double:
                    var doubleValue = NumericUtils.PrefixCodedToDouble(fieldText);
                    check = NumericUtils.DoubleToPrefixCoded(doubleValue);
                    if (check != fieldText)
                        return null;
                    return Convert.ToString(doubleValue, CultureInfo.InvariantCulture);
                case IndexValueType.DateTime:
                    var ticksValue = NumericUtils.PrefixCodedToLong(fieldText);
                    check = NumericUtils.LongToPrefixCoded(ticksValue);
                    if (check != fieldText)
                        return null;
                    var d = new DateTime(ticksValue);
                    if (d.Hour == 0 && d.Minute == 0 && d.Second == 0)
                        return d.ToString("yyyy-MM-dd");
                    if (d.Second == 0)
                        return d.ToString("yyyy-MM-dd HH:mm");
                    return d.ToString("yyyy-MM-dd HH:mm:ss");
                default:
                    throw new NotSupportedException("Unknown IndexFieldType: " + fieldType);
            }
        }


        /* ===================================================================================== SQL DATA HANDLING */

        private static async Task<TimestampData> GetTimestampDataForOneNodeIntegrityCheckAsync(string path, int[] excludedNodeTypeIds)
        {
            var checkNodeSql = "SELECT N.NodeId, V.VersionId, CONVERT(bigint, n.timestamp) NodeTimestamp, CONVERT(bigint, v.timestamp) VersionTimestamp, N.LastMajorVersionId, N.LastMinorVersionId from Versions V join Nodes N on V.NodeId = N.NodeId WHERE N.Path = '{0}' COLLATE Latin1_General_CI_AS";
            if (excludedNodeTypeIds != null && excludedNodeTypeIds.Length > 0)
                checkNodeSql += $" AND N.NodeTypeId NOT IN ({string.Join(", ", excludedNodeTypeIds)})";

            var sql = string.Format(checkNodeSql, path);

            using var ctx = new MsSqlDataContext(ConnectionStrings.ConnectionString, 
                DataOptions.GetLegacyConfiguration(), CancellationToken.None);

            return await ctx.ExecuteReaderAsync(sql, async (reader, cancel) =>
            {
                cancel.ThrowIfCancellationRequested();
                TimestampData dbNode = null;
                if (await reader.ReadAsync(cancel).ConfigureAwait(false))
                {
                    dbNode = new TimestampData
                    {
                        NodeId = reader.GetSafeInt32(reader.GetOrdinal("NodeId")),
                        VersionId = reader.GetSafeInt32(reader.GetOrdinal("VersionId")),
                        NodeTimestamp = reader.GetSafeInt64(reader.GetOrdinal("NodeTimestamp")),
                        VersionTimestamp = reader.GetSafeInt64(reader.GetOrdinal("VersionTimestamp")),
                        LastMajorVersionId = reader.GetSafeInt32(reader.GetOrdinal("LastMajorVersionId")),
                        LastMinorVersionId = reader.GetSafeInt32(reader.GetOrdinal("LastMinorVersionId")),
                    };
                }
                return dbNode;
            }).ConfigureAwait(false);
        }
        private static async Task<TimestampData[]> GetTimestampDataForRecursiveIntegrityCheckAsync(string path, int[] excludedNodeTypeIds)
        {
            var typeFilter = excludedNodeTypeIds != null && excludedNodeTypeIds.Length > 0
                ? $"N.NodeTypeId NOT IN ({string.Join(", ", excludedNodeTypeIds)})"
                : null;

            string sql;
            if (path == null)
            {
                sql = "SELECT N.NodeId, V.VersionId, CONVERT(bigint, n.timestamp) NodeTimestamp, CONVERT(bigint, v.timestamp) VersionTimestamp, N.LastMajorVersionId, N.LastMinorVersionId from Versions V join Nodes N on V.NodeId = N.NodeId";
                if (!string.IsNullOrEmpty(typeFilter))
                    sql += " WHERE " + typeFilter;
            }
            else
            {
                sql = string.Format("SELECT N.NodeId, V.VersionId, CONVERT(bigint, n.timestamp) NodeTimestamp, CONVERT(bigint, v.timestamp) VersionTimestamp, N.LastMajorVersionId, N.LastMinorVersionId from Versions V join Nodes N on V.NodeId = N.NodeId WHERE (N.Path = '{0}' COLLATE Latin1_General_CI_AS OR N.Path LIKE REPLACE('{0}', '_', '[_]') + '/%' COLLATE Latin1_General_CI_AS)", path);
                if (!string.IsNullOrEmpty(typeFilter))
                    sql += " AND " + typeFilter;
            }

            using var ctx = new MsSqlDataContext(ConnectionStrings.ConnectionString,
                DataOptions.GetLegacyConfiguration(), CancellationToken.None);

            return await ctx.ExecuteReaderAsync(sql, async (reader, cancel) =>
            {
                cancel.ThrowIfCancellationRequested();
                var result = new List<TimestampData>();
                while (await reader.ReadAsync(cancel).ConfigureAwait(false))
                {
                    result.Add(new TimestampData
                    {
                        NodeId = reader.GetSafeInt32(reader.GetOrdinal("NodeId")),
                        VersionId = reader.GetSafeInt32(reader.GetOrdinal("VersionId")),
                        NodeTimestamp = reader.GetSafeInt64(reader.GetOrdinal("NodeTimestamp")),
                        VersionTimestamp = reader.GetSafeInt64(reader.GetOrdinal("VersionTimestamp")),
                        LastMajorVersionId = reader.GetSafeInt32(reader.GetOrdinal("LastMajorVersionId")),
                        LastMinorVersionId = reader.GetSafeInt32(reader.GetOrdinal("LastMinorVersionId")),
                    });
                }
                return result.ToArray();
            }).ConfigureAwait(false);
        }

        /* ================================================================== COPIED FROM Lucene29LocalQueryEngine */

        private LucQuery Compile(SnQuery query, IQueryContext context)
        {
            var indexingEngine = (ILuceneIndexingEngine)IndexManager.IndexingEngine;
            var analyzer = indexingEngine.GetAnalyzer();
            var visitor = new SnQueryToLucQueryVisitor(analyzer, context);
            visitor.Visit(query.QueryTree);

            var result = LucQuery.Create(visitor.Result, indexingEngine.LuceneSearchManager);
            result.Skip = query.Skip;
            result.Top = query.Top;
            result.SortFields = query.Sort?.Select(s => CreateSortField(s.FieldName, s.Reverse)).ToArray() ?? new SortField[0];
            result.EnableAutofilters = query.EnableAutofilters;
            result.EnableLifespanFilter = query.EnableLifespanFilter;
            result.QueryExecutionMode = query.QueryExecutionMode;
            result.CountOnly = query.CountOnly;
            result.CountAllPages = query.CountAllPages;

            return result;
        }
        private static SortField CreateSortField(string fieldName, bool reverse)
        {
            var info = SearchManager.GetPerFieldIndexingInfo(fieldName);
            var sortType = SortField.STRING;
            if (info != null)
            {
                fieldName = info.IndexFieldHandler.GetSortFieldName(fieldName);

                switch (info.IndexFieldHandler.IndexFieldType)
                {
                    case IndexValueType.Bool:
                    case IndexValueType.String:
                    case IndexValueType.StringArray:
                        sortType = SortField.STRING;
                        break;
                    case IndexValueType.Int:
                        sortType = SortField.INT;
                        break;
                    case IndexValueType.DateTime:
                    case IndexValueType.Long:
                        sortType = SortField.LONG;
                        break;
                    case IndexValueType.Float:
                        sortType = SortField.FLOAT;
                        break;
                    case IndexValueType.Double:
                        sortType = SortField.DOUBLE;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            if (sortType == SortField.STRING)
                return new SortField(fieldName, Thread.CurrentThread.CurrentCulture, reverse);
            return new SortField(fieldName, sortType, reverse);
        }

    }
}
