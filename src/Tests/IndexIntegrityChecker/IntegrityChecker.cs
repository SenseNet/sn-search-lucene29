using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Schema;
using SenseNet.ContentRepository.Search;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.ContentRepository.Storage.Data.MsSqlClient;
using SenseNet.Diagnostics;
using SenseNet.Search;
using SenseNet.Search.Indexing;
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
                    if (path.ToLower() == SenseNet.ContentRepository.Repository.RootPath.ToLower())
                        path = null;
                return new IndexIntegrityChecker().CheckRecurse(path);
            }
            return new IndexIntegrityChecker().CheckNode(path ?? SenseNet.ContentRepository.Repository.RootPath);
        }

        /*==================================================================================== Instance part */

        private IEnumerable<Difference> CheckNode(string path)
        {
            var result = new List<Difference>();
            using (var readerFrame = LuceneSearchManager.GetIndexReaderFrame())
            {
                var ixreader = readerFrame.IndexReader;
                //var sql = String.Format(checkNodeSql, path);
                //var proc = SenseNet.ContentRepository.Storage.Data.DataProvider.CreateDataProcedure(sql);
                //proc.CommandType = System.Data.CommandType.Text;
                var docids = new List<int>();
                var timestampData = GetTimestampDataForOneNodeIntegrityCheckAsync(path, GetExcludedNodeTypeIds())
                    .ConfigureAwait(false).GetAwaiter().GetResult();


                //using (var dbreader = proc.ExecuteReader())
                //{
                //    while (dbreader.Read())
                //    {
                //        var docid = CheckDbAndIndex(timestampData, ixReader, result);
                //        if (docid >= 0)
                //            docids.Add(docid);
                //    }
                //}
                var dbDocId = CheckDbAndIndex(timestampData, ixreader, result);
                if (dbDocId >= 0)
                    docids.Add(dbDocId);

                var scoredocs = GetDocsUnderTree(path, false);
                foreach (var scoredoc in scoredocs)
                {
                    var docid = scoredoc.Doc;
                    var doc = ixreader.Document(docid);
                    if (!docids.Contains(docid))
                    {
                        result.Add(new Difference(IndexDifferenceKind.NotInDatabase)
                        {
                            DocId = scoredoc.Doc,
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
                    var ixreader = readerFrame.IndexReader;
                    _numDocs = ixreader.NumDocs() + ixreader.NumDeletedDocs();
                    var x = _numDocs / intSize;
                    var y = _numDocs % intSize;
                    _docBits = new int[x + (y > 0 ? 1 : 0)];
                    if (path == null)
                    {
                        if (y > 0)
                        {
                            var q = 0;
                            for (int i = 0; i < y; i++)
                                q += 1 << i;
                            _docBits[_docBits.Length - 1] = q ^ (-1);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < _docBits.Length; i++)
                            _docBits[i] = -1;
                        var scoredocs = GetDocsUnderTree(path, true);
                        for (int i = 0; i < scoredocs.Length; i++)
                        {
                            var docid = scoredocs[i].Doc;
                            _docBits[docid / intSize] ^= 1 << docid % intSize;
                        }
                    }


                    //var proc = DataStore.DataProvider.GetTimestampDataForRecursiveIntegrityCheck(path, GetExcludedNodeTypeIds());
                    var timestampData = GetTimestampDataForRecursiveIntegrityCheckAsync(path, GetExcludedNodeTypeIds())
                        .ConfigureAwait(false).GetAwaiter().GetResult();

                    //using (var dbreader = proc.ExecuteReader())
                    //{
                    //    while (dbreader.Read())
                    //    {
                    //        if ((++progress % 10000) == 0)
                    //            SnTrace.Index.Write("Index Integrity Checker: CheckDbAndIndex: progress={0}/{1}, diffs:{2}", progress, _numDocs, result.Count);

                    //        var docid = CheckDbAndIndex(dbreader, ixReader, result);
                    //        if (docid > -1)
                    //            _docBits[docid / intSize] |= 1 << docid % intSize;
                    //    }
                    //}
                    foreach (var item in timestampData)
                    {
                        var docid = CheckDbAndIndex(item, ixreader, result);
                        if (docid > -1)
                            _docBits[docid / intSize] |= 1 << docid % intSize;
                    }


                    for (int i = 0; i < _docBits.Length; i++)
                    {
                        if (_docBits[i] != -1)
                        {
                            var bits = _docBits[i];
                            for (int j = 0; j < intSize; j++)
                            {
                                if ((bits & (1 << j)) == 0)
                                {
                                    var docid = i * intSize + j;
                                    if (docid >= _numDocs)
                                        break;
                                    if (!ixreader.IsDeleted(docid))
                                    {
                                        var doc = ixreader.Document(docid);
                                        if (!IsCommitDocument(doc))
                                        {
                                            result.Add(new Difference(IndexDifferenceKind.NotInDatabase)
                                            {
                                                DocId = docid,
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

            var termDocs = ixReader.TermDocs(new Term(IndexFieldName.VersionId, Lucene.Net.Util.NumericUtils.IntToPrefixCoded(versionId)));
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
                    if (isLastDraft !=  IndexValue.Yes)
                    {
                        var latestDocs = ixReader.TermDocs(new Term(IndexFieldName.NodeId, Lucene.Net.Util.NumericUtils.IntToPrefixCoded(nodeId)));
                        Lucene.Net.Documents.Document latestDoc = null;
                        while (latestDocs.Next())
                        {
                            var latestDocId = latestDocs.Doc();
                            var d = ixReader.Document(latestDocId);
                            if (d.Get(IndexFieldName.IsLastDraft) !=  IndexValue.Yes)
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
                    .Select(ct => ActiveSchema.NodeTypes[ct.Name].Id).ToArray();
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


        /* ===================================================================================== SQL DATA HANDLING */

        private static async Task<TimestampData> GetTimestampDataForOneNodeIntegrityCheckAsync(string path, int[] excludedNodeTypeIds)
        {
            string checkNodeSql = "SELECT N.NodeId, V.VersionId, CONVERT(bigint, n.timestamp) NodeTimestamp, CONVERT(bigint, v.timestamp) VersionTimestamp, N.LastMajorVersionId, N.LastMinorVersionId from Versions V join Nodes N on V.NodeId = N.NodeId WHERE N.Path = '{0}' COLLATE Latin1_General_CI_AS";
            if (excludedNodeTypeIds != null && excludedNodeTypeIds.Length > 0)
                checkNodeSql += $" AND N.NodeTypeId NOT IN ({string.Join(", ", excludedNodeTypeIds)})";

            var sql = string.Format(checkNodeSql, path);

            using (var ctx = new MsSqlDataContext(CancellationToken.None))
            {
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
        }
        private static async Task<TimestampData[]> GetTimestampDataForRecursiveIntegrityCheckAsync(string path, int[] excludedNodeTypeIds)
        {
            string typeFilter = excludedNodeTypeIds != null && excludedNodeTypeIds.Length > 0
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

            using (var ctx = new MsSqlDataContext(CancellationToken.None))
            {
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
