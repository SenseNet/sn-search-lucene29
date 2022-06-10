using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Contrib.Regex;
using Lucene.Net.Analysis;
using Lucene.Net.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.ContentRepository.Search;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Index.Configuration;
using SenseNet.Search.Querying;
using SenseNet.Search.Querying.Parser;
using SenseNet.Search.Querying.Parser.Predicates;
using SenseNet.Search.Tests.Implementations;
using SenseNet.Tests.Core;
using SenseNet.Tests.Core.Implementations;

namespace SenseNet.Search.Lucene29.Tests
{
    [TestClass]
    public class Lucene29RegexTests : TestBase
    {
        private class TestQueryContext : IQueryContext
        {
            private readonly Dictionary<string, IPerFieldIndexingInfo> _indexingInfoTable;

            public TestQueryContext(Dictionary<string, IPerFieldIndexingInfo> indexingInfoTable)
            {
                _indexingInfoTable = indexingInfoTable;
            }

            public IPerFieldIndexingInfo GetPerFieldIndexingInfo(string fieldName)
            {
                if (_indexingInfoTable.TryGetValue(fieldName, out var result))
                    return result;
                return null;
            }

            public QuerySettings Settings { get; } = QuerySettings.AdminSettings;
            public int UserId => Identifiers.SystemUserId;
            public IQueryEngine QueryEngine => null;
            public IMetaQueryEngine MetaQueryEngine => null;
        }

        [TestMethod, TestCategory("IR")]
        public void L29_Compiler_Regex()
        {
            var regex = "/[a-zA-Z]{3}\\S{1}\\w*/";
            var escapedRegex = "/" + regex + "/"; //.Replace(@"\", @"\\");
            var fieldName = "Name";
            var queryText = $"{fieldName}:'{escapedRegex}'";
            var nameIndexingInfo = new TestPerfieldIndexingInfoString();
            nameIndexingInfo.IndexFieldHandler = new LowerStringIndexHandler();
            var indexingInfo = new Dictionary<string, IPerFieldIndexingInfo> { { "Name", nameIndexingInfo } };

            var value = new IndexValue(escapedRegex);
            var predicate = new SimplePredicate(fieldName, value);
            var snQuery = SnQuery.Create(predicate);

            // ACTION
            var analyzer = new KeywordAnalyzer();
            var context = new TestQueryContext(indexingInfo);
            var visitor = new SnQueryToLucQueryVisitor(analyzer, context);
            visitor.Visit(snQuery.QueryTree);
            var lucQuery = visitor.Result;

            // ASSERT
            Assert.IsInstanceOfType(lucQuery, typeof(RegexQuery));
            var termQuery = lucQuery as RegexQuery;
            Assert.IsNotNull(termQuery);
            var term = termQuery.GetTerm();
            Assert.AreEqual(regex, term.text_ForNUnit);
        }
        [TestMethod, TestCategory("IR")] // 11 tests
        public void Luc29_Compiler_Regex_ToString()
        {
            // ReSharper disable once JoinDeclarationAndInitializer
            Query q;
            q = RegexQueryTest("Name:/abc/", "Name:\"/abc/\""); Assert.IsTrue(q is RegexQuery);
            q = RegexQueryTest("Name:'/abc/'", "Name:\"/abc/\""); Assert.IsTrue(q is RegexQuery);
            q = RegexQueryTest("Name:\"/abc/\"", "Name:\"/abc/\""); Assert.IsTrue(q is RegexQuery);
            q = RegexQueryTest("Name:\"/[\\\\W]{1,2}/\""); Assert.IsTrue(q is RegexQuery);
        }

        private Query RegexQueryTest(string queryText, string expected = null)
        {
            expected = expected ?? queryText;

            var nameIndexingInfo = new TestPerfieldIndexingInfoString();
            nameIndexingInfo.IndexFieldHandler = new LowerStringIndexHandler();
            var indexingInfo = new Dictionary<string, IPerFieldIndexingInfo>
            {
                {"Name", nameIndexingInfo},
            };

            var queryContext = new Search.Tests.Implementations.TestQueryContext(QuerySettings.Default, 0, indexingInfo);
            var parser = new CqlParser();
            var snQuery = parser.Parse(queryText, queryContext);

            var analyzers = indexingInfo.ToDictionary(kvp => kvp.Key, kvp => Lucene29LocalIndexingEngine.GetAnalyzer(kvp.Value));
            var indexFieldTypes = indexingInfo.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.IndexFieldHandler.IndexFieldType);

            var sm = new LuceneSearchManager(new IndexDirectory());
            sm.SetIndexingInfo(analyzers, indexFieldTypes);

            var analyzer = new KeywordAnalyzer();
            var context = new TestQueryContext(indexingInfo);
            var visitor = new SnQueryToLucQueryVisitor(analyzer, context);
            visitor.Visit(snQuery.QueryTree);
            var query = visitor.Result;

            var lqVisitor = new LucQueryToStringVisitor(sm);
            lqVisitor.Visit(query);
            var actual = lqVisitor.ToString();

            Assert.AreEqual(expected, actual);

            return query;
        }

    }
}
