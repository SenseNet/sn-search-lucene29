using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Lucene.Net.Index;
using Lucene.Net.Util;
using Newtonsoft.Json;
using SenseNet.Search.Indexing;

namespace SenseNet.Search.Lucene29
{
    public class IndexExplorer
    {
        private readonly LuceneSearchManager _searchManager;

        public IndexExplorer(LuceneSearchManager searchManager)
        {
            _searchManager = searchManager;
        }

        public IndexProperties GetIndexProperties()
        {
            IndexingActivityStatus commitUserData;
            var index = new Dictionary<string, int>();
            var versionIds = new List<int>();
            using (var readerFrame = _searchManager.GetIndexReaderFrame())
            {
                var ixReader = readerFrame.IndexReader;
                commitUserData = _searchManager.ReadActivityStatusFromIndex();
                var terms = ixReader.Terms();
                while (terms.Next())
                {
                    var term = terms.Term();
                    var field = term.Field();

                    var termDocs = ixReader.TermDocs(term);
                    var docs = 0;
                    int doc;
                    while (termDocs.Next())
                    {
                        if (!ixReader.IsDeleted((doc = termDocs.Doc())))
                        {
                            docs++;
                            if (field == "VersionId")
                            {
                                var text = GetTermText(term);
                                if (text == null)
                                    continue;
                                if (int.TryParse(text, out var versionId))
                                    versionIds.Add(versionId);
                            }
                        }
                    }

                    if (index.TryGetValue(field, out var count))
                        index[field] = count + 1;
                    else
                        index[field] = 1;
                }
            }

            var result = new IndexProperties
            {
                IndexingActivityStatus = commitUserData,
                FieldInfo = index.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value),
                VersionIds = versionIds.Distinct().OrderBy(x => x).ToArray()
            };

            return result;
        }

        public IDictionary<string, IDictionary<string, List<int>>> GetInvertedIndex()
        {
            var result = new Dictionary<string, IDictionary<string, List<int>>>();

            using (var readerFrame = _searchManager.GetIndexReaderFrame())
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

                    if (!result.TryGetValue(field, out var fieldData))
                        result.Add(field, fieldData = new Dictionary<string, List<int>>());

                    if (!fieldData.TryGetValue(text, out var termData))
                        fieldData.Add(text, termData = new List<int>());

                    var termDocs = ixReader.TermDocs(term);
                    int doc;
                    while (termDocs.Next())
                        if (!ixReader.IsDeleted((doc = termDocs.Doc())))
                            termData.Add(doc);
                    termData.Sort();
                }
            }

            return result;
        }

        public IDictionary<string, List<int>> GetInvertedIndex(string fieldName)
        {
            var fieldData = new Dictionary<string, List<int>>();

            using (var readerFrame = _searchManager.GetIndexReaderFrame())
            {
                var ixReader = readerFrame.IndexReader;
                var terms = ixReader.Terms(/*new Term(fieldName)*/);
                while (terms.Next())
                {
                    var term = terms.Term();
                    var field = term.Field();
                    if (field != fieldName)
                        continue;

                    var text = GetTermText(term) ?? string.Empty;

                    if (!fieldData.TryGetValue(text, out var termData))
                        fieldData.Add(text, termData = new List<int>());

                    var termDocs = ixReader.TermDocs(term);
                    int doc;
                    while (termDocs.Next())
                        if (!ixReader.IsDeleted((doc = termDocs.Doc())))
                            termData.Add(doc);
                    termData.Sort();
                }
            }

            return fieldData;
        }

        public IDictionary<string, string> GetIndexDocumentByVersionId(int versionId)
        {
            using (var readerFrame = _searchManager.GetIndexReaderFrame())
            {
                var ixReader = readerFrame.IndexReader;
                var term = new Term("VersionId", NumericUtils.IntToPrefixCoded(versionId));
                var termDocs = ixReader.TermDocs(term);
                var docs = new List<int>();
                while (termDocs.Next())
                    docs.Add(termDocs.Doc());
                var doc = docs.FirstOrDefault();
                return GetIndexDocumentByDocumentId(doc, ixReader);
            }
        }

        public IDictionary<string, string> GetIndexDocumentByDocumentId(int documentId)
        {
            using (var readerFrame = _searchManager.GetIndexReaderFrame())
                return GetIndexDocumentByDocumentId(documentId, readerFrame.IndexReader);
        }

        public IDictionary<string, string> GetIndexDocumentByDocumentId(int documentId, IndexReader ixReader)
        {
            var result = new Dictionary<string, string>();

            var terms = ixReader.Terms();
            while (terms.Next())
            {
                var term = terms.Term();
                var field = term.Field();

                var termDocs = ixReader.TermDocs(term);
                int doc;
                while (termDocs.Next())
                {
                    doc = termDocs.Doc();
                    if (doc == documentId && !ixReader.IsDeleted(doc))
                    {
                        var text = GetTermText(term);
                        if (text == null)
                            continue;
                        if (result.TryGetValue(field, out var value))
                            result[field] = value + ", " + text;
                        else
                            result.Add(field, text);
                    }
                }
            }

            return result;
        }

        private string GetTermText(Term term)
        {
            var fieldName = term.Field();
            var fieldText = term.Text();
            if (fieldText == null)
                return null;

            if (!_searchManager.IndexFieldTypeInfo.TryGetValue(fieldName, out var fieldType))
            {
                if (fieldName == "NodeTimestamp" || fieldName == "VersionTimestamp")
                    fieldType = IndexValueType.Long;
                else
                    fieldType = default(IndexValueType);
            }

            string check;
            switch (fieldType)
            {
                case IndexValueType.Bool:
                case IndexValueType.String:
                case IndexValueType.StringArray:
                    return fieldText;
                case IndexValueType.Int:
                case IndexValueType.IntArray:
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

    }
}
