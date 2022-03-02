using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lucene.Net.Analysis;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.ContentRepository.Storage.Schema;
using SenseNet.Search.Indexing;
using SenseNet.Search.Querying;
using SenseNet.Testing;
using SenseNet.Tests.Core;
using Directory = System.IO.Directory;

namespace SenseNet.Search.Lucene29.Tests
{
    [ExcludeFromCodeCoverage]
    [TestClass]
    public class IndexingTests : TestBase
    {
        [TestMethod]
        public async Task Indexing_Simple()
        {
            var indexFolderName = $"Test_{nameof(Indexing_Simple)}_{Guid.NewGuid()}";
            var indexDirectory = new IndexDirectory(indexFolderName);
            var analyzer = new KeywordAnalyzer();
            var indexingEngine = new Lucene29LocalIndexingEngine(new IndexDirectory(indexFolderName));
            var searchEngine = new Lucene29SearchEngine(indexingEngine, new Lucene29LocalQueryEngine());

            var dir = FSDirectory.Open(new DirectoryInfo(indexDirectory.CurrentDirectory));
            var writer = new IndexWriter(dir, analyzer, true, IndexWriter.MaxFieldLength.UNLIMITED);

            var acc = new ObjectAccessor(indexingEngine.LuceneSearchManager);
            acc.SetField("_writer", writer);

            var m = IndexingMode.NotAnalyzed;
            var s = IndexStoringMode.No;
            var tv = IndexTermVector.No;
            var date = new DateTime(1925, 08, 15, 0, 0, 0);

            var additions = new[]
            {
                new IndexDocument
                {
                    new IndexField("Name", "OscarPeterson", m, s, tv),
                    new IndexField("Tags", new[] {"tag1", "tag2", "tag3"}, m, s, tv),
                    new IndexField("Id", 42, m, s, tv),
                    new IndexField("Ids", new[] {1, 2, 4, 6}, m, s, tv),
                    new IndexField("Bool", true, m, s, tv),
                    new IndexField("Ticks", date.Ticks, m, s, tv),
                    new IndexField("Pi", 3.14f, m, s, tv),
                    new IndexField("Sqrt2", 1.414d, m, s, tv),
                    new IndexField("Birth", date, m, s, tv),
                },
            };
            var indexFieldTypeInfo = new Dictionary<string, IndexValueType>
            {
                {"Name", IndexValueType.String},
                {"Tags", IndexValueType.StringArray},
                {"Id", IndexValueType.Int},
                {"Ids", IndexValueType.IntArray},
                {"Bool", IndexValueType.Bool},
                {"Ticks", IndexValueType.Long},
                {"Pi", IndexValueType.Float},
                {"Sqrt2", IndexValueType.Double},
                {"Birth", IndexValueType.DateTime},
            };

            var expectation = new Dictionary<int, Dictionary<string, string>>
            {
                {0, new Dictionary<string, string> {
                    {"Name", "OscarPeterson"},
                    {"Tags", "tag1, tag2, tag3"},
                    {"Id", "42"},
                    {"Ids", "1, 2, 4, 6"},
                    {"Bool", "yes"},
                    {"Ticks", date.Ticks.ToString()},
                    {"Pi", "3.14"},
                    {"Sqrt2", "1.414"},
                    {"Birth", "1925-08-15"},
                }},
            };

            Dictionary<int, Dictionary<string, string>> docs = null;

            // ACTION
            try
            {
                await indexingEngine.WriteIndexAsync(null, null, additions, CancellationToken.None);

                var reader = writer.GetReader();
                docs = GetIndexDocs(indexDirectory.CurrentDirectory, indexFieldTypeInfo, reader);
            }
            finally
            {
                writer.Commit();
                writer.Close();
                writer.Dispose();
            }

            // ASSERT
            docs.Should().BeEquivalentTo(expectation);
            foreach (var key in docs.Keys)
                docs[key].Should().BeEquivalentTo(expectation[key]);
        }
        [TestMethod]
        public async Task Indexing_IntArray()
        {
            var indexFolderName = $"Test_{nameof(Indexing_IntArray)}_{Guid.NewGuid()}";
            var indexDirectory = new IndexDirectory(indexFolderName);
            var analyzer = new KeywordAnalyzer();
            var indexingEngine = new Lucene29LocalIndexingEngine(new IndexDirectory(indexFolderName));
            var searchEngine = new Lucene29SearchEngine(indexingEngine, new Lucene29LocalQueryEngine());

            var dir = FSDirectory.Open(new DirectoryInfo(indexDirectory.CurrentDirectory));
            var writer = new IndexWriter(dir, analyzer, true, IndexWriter.MaxFieldLength.UNLIMITED);

            var acc = new ObjectAccessor(indexingEngine.LuceneSearchManager);
            acc.SetField("_writer", writer);

            var m = IndexingMode.NotAnalyzed;
            var s = IndexStoringMode.No;
            var tv = IndexTermVector.No;
            var date = new DateTime(1925, 08, 15, 0, 0, 0);

            var additions = new[]
            {
                new IndexDocument
                {
                    new IndexField("RefEmpty", Array.Empty<int>(), m, s, tv),
                    new IndexField("RefZero", new []{0}, m, s, tv),
                    new IndexField("RefValue", new []{142}, m, s, tv),
                    new IndexField("RefMulti", new []{142,143}, m, s, tv),
                }
            };
            var indexFieldTypeInfo = new Dictionary<string, IndexValueType>
            {
                {"RefEmpty", IndexValueType.IntArray},
                {"RefZero", IndexValueType.IntArray},
                {"RefValue", IndexValueType.IntArray},
                {"RefMulti", IndexValueType.IntArray},
            };

            var expectation = new Dictionary<int, Dictionary<string, string>>
            {
                {0, new Dictionary<string, string> {
                    //{"RefEmpty", "empty"},
                    {"RefZero", "0"},
                    {"RefValue", "142"},
                    {"RefMulti", "142, 143"},
                }},
            };

            Dictionary<int, Dictionary<string, string>> docs = null;

            // ACTION
            try
            {
                await indexingEngine.WriteIndexAsync(null, null, additions, CancellationToken.None);

                var reader = writer.GetReader();
                docs = GetIndexDocs(indexDirectory.CurrentDirectory, indexFieldTypeInfo, reader);
            }
            finally
            {
                writer.Commit();
                writer.Close();
                writer.Dispose();
            }

            // ASSERT
            docs.Should().BeEquivalentTo(expectation);
        }
        [TestMethod]
        public async Task Indexing_Delete()
        {
            var indexFolderName = $"Test_{nameof(Indexing_Delete)}_{Guid.NewGuid()}";
            var indexDirectory = new IndexDirectory(indexFolderName);
            var analyzer = new KeywordAnalyzer();
            var indexingEngine = new Lucene29LocalIndexingEngine(new IndexDirectory(indexFolderName));
            var searchEngine = new Lucene29SearchEngine(indexingEngine, new Lucene29LocalQueryEngine());

            var dir = FSDirectory.Open(new DirectoryInfo(indexDirectory.CurrentDirectory));
            var writer = new IndexWriter(dir, analyzer, true, IndexWriter.MaxFieldLength.UNLIMITED);

            var acc = new ObjectAccessor(indexingEngine.LuceneSearchManager);
            acc.SetField("_writer", writer);

            var m = IndexingMode.NotAnalyzed;
            var s = IndexStoringMode.No;
            var tv = IndexTermVector.No;
            var date = new DateTime(1925, 08, 15, 0, 0, 0);

            var additions = new[]
            {
                new IndexDocument
                {
                    new IndexField("Name", "Name1", m, s, tv),
                    new IndexField("Id", 44, m, s, tv),
                    new IndexField("Ids", new[] {11, 12, 14, 16}, m, s, tv),
                },
                new IndexDocument
                {
                    new IndexField("Name", "Name2", m, s, tv),
                    new IndexField("Id", 43, m, s, tv),
                    new IndexField("Ids", new[] {1, 2, 4, 6}, m, s, tv),
                },
                new IndexDocument
                {
                    new IndexField("Name", "Name3", m, s, tv),
                    new IndexField("Id", 42, m, s, tv),
                    new IndexField("Ids", new[] {1, 2, 4, 6}, m, s, tv),
                },
            };
            var indexFieldTypeInfo = new Dictionary<string, IndexValueType>
            {
                {"Name", IndexValueType.String},
                {"Id", IndexValueType.Int},
                {"Ids", IndexValueType.IntArray},
            };

            Dictionary<int, Dictionary<string, string>> docs = null;
            string[] idArray = null;

            try
            {
                await indexingEngine.WriteIndexAsync(null, null, additions, CancellationToken.None);

                var reader = writer.GetReader();
                docs = GetIndexDocs(indexDirectory.CurrentDirectory, indexFieldTypeInfo, reader);
                idArray = GetIdArray(docs);
            }
            finally
            {
                writer.Commit();
            }

            idArray.Should().Equal(new[] { "42", "43", "44" });

            // ACTION-1 Delete by a member of intArray
            try
            {
                var deletions = new[] {new SnTerm("Ids", new[] {12})};
                await indexingEngine.WriteIndexAsync(deletions, null, null, CancellationToken.None);

                var reader = writer.GetReader();
                docs = GetIndexDocs(indexDirectory.CurrentDirectory, indexFieldTypeInfo, reader);
                idArray = GetIdArray(docs);
            }
            finally
            {
                writer.Commit();
            }
            // ASSERT-1
            idArray.Should().Equal(new[] { "42", "43" });


            // ACTION-2 Delete by Id
            try
            {
                var deletions = new[] {new SnTerm("Id", 43)};
                await indexingEngine.WriteIndexAsync(deletions, null, null, CancellationToken.None);

                var reader = writer.GetReader();
                docs = GetIndexDocs(indexDirectory.CurrentDirectory, indexFieldTypeInfo, reader);
                idArray = GetIdArray(docs);
            }
            finally
            {
                writer.Commit();
                writer.Close();
                writer.Dispose();
            }
            // ASSERT-2
            idArray.Should().Equal(new[] { "42" });
        }

        /* ==================================================================================================== */

        [ClassInitialize]
        public static void CleanupOldDirectories(TestContext testContext)
        {
            // Need to run before tests because the indexes cannot be deleted safely in the same appDomain.
            var containerPath = Path.GetDirectoryName(new IndexDirectory("any").CurrentDirectory);
            foreach (var path in Directory.GetDirectories(containerPath))
                Directory.Delete(path, true);
        }

        public string[] GetIdArray(Dictionary<int, Dictionary<string, string>> docs)
        {
            return GetFieldValueArray(docs, "Id");
        }
        public string[] GetFieldValueArray(Dictionary<int, Dictionary<string, string>> docs, string fieldName)
        {
            return docs.Values.Select(x => x[fieldName]).OrderBy(x => x).ToArray();
        }

        public Dictionary<string, Dictionary<string, string>> GetRawIndex(string savedIndexDir, IDictionary<string, IndexValueType> indexFieldTypeInfo, IndexReader ixReader)
        {
            var index = new Dictionary<string, Dictionary<string, string>>();

            var terms = ixReader.Terms();
            while (terms.Next())
            {
                var term = terms.Term();
                var field = term.Field();
                var text = GetTermText(term, indexFieldTypeInfo);
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

            return index;
        }

        public Dictionary<int, Dictionary<string, string>> GetIndexDocs(string savedIndexDir, IDictionary<string, IndexValueType> indexFieldTypeInfo, IndexReader ixReader)
        {
            var documents = new Dictionary<int, Dictionary<string, string>>();

            var terms = ixReader.Terms();
            while (terms.Next())
            {
                var term = terms.Term();
                var field = term.Field();
                var text = GetTermText(term, indexFieldTypeInfo);
                if (text == null)
                    continue;

                var termDocs = ixReader.TermDocs(term);
                int doc;
                while (termDocs.Next())
                    if (!ixReader.IsDeleted((doc = termDocs.Doc())))
                        AddFieldToDocument(documents, doc, field, text);
            }

            return documents;
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
        private string GetTermText(Term term, IDictionary<string, IndexValueType> indexFieldTypeInfo)
        {
            var fieldName = term.Field();
            var fieldText = term.Text();
            if (fieldText == null)
                return null;

            if (!indexFieldTypeInfo.TryGetValue(fieldName, out var fieldType))
                fieldType = default(IndexValueType);

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
