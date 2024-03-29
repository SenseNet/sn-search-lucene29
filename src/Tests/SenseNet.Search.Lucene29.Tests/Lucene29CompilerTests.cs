﻿using System;
using System.Globalization;
using System.Linq;
using Lucene.Net.Search;
using Lucene.Net.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Search;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.Extensions.DependencyInjection;
using SenseNet.Search.Indexing;
using SenseNet.Search.Querying;
using SenseNet.Search.Querying.Parser;
using SenseNet.Search.Tests.Implementations;
using SenseNet.Tests;
using SenseNet.Tests.Core;
using SenseNet.Tests.Core.Implementations;

namespace SenseNet.Search.Lucene29.Tests
{
    [TestClass]
    public class Lucene29CompilerTests : TestBase
    {
        [TestInitialize]
        public void Initialize()
        {
            var searchManager = new SearchManager(null);

            var services = new ServiceCollection()
                .AddSingleton<ISearchManager>(searchManager)
                .AddSingleton<IIndexManager>(new IndexManager(null, searchManager, null,
                    NullLogger<IndexManager>.Instance))
                .BuildServiceProvider();

            Providers.Instance = new Providers(services);

            var indexingEngine = new Lucene29LocalIndexingEngine();
            var queryEngine = new Lucene29LocalQueryEngine();
            Providers.Instance.SearchEngine = new Lucene29SearchEngine(indexingEngine, queryEngine,
                NullLoggerFactory.Instance.CreateLogger<Lucene29SearchEngine>());
        }

        [TestMethod, TestCategory("IR")] // 38 tests
        public void Search_Compiler_Luc29_OriginalTests()
        {
            Test("value", "_Text:value");
            Test("VALUE", "_Text:value");
            Test("Value", "_Text:value");
            Test("Value1", "_Text:value1");
            Test("-Value1", "-_Text:value1");
            Test("+Value1", "+_Text:value1");
            Test("Value1 -Value2 +Value3 Value4", "_Text:value1 -_Text:value2 +_Text:value3 _Text:value4");
            Test("Field1:Value1", "Field1:value1");
            Test("#Field1:value1");
            Test("-Field1:value1");
            Test("+Field1:value1");
            Test("Field1:value1 Field2:value2 Field3:value3");
            Test("F1:v1 -F2:v2 +F3:v3 F4:v4");
            Test("f1:v1 f2:v2");
            Test("f1:v1 f2:v2 (f3:v3 f4:v4 (f5:v5 f6:v6))");
            Test("f1:v1 (f2:v2 (f3:v3 f4:v4))");
            Test("aaa AND +bbb", "+_Text:aaa +_Text:bbb");
            Test("te?t", "_Text:te?t");
            Test("test*", "_Text:test*");
            Test("te*t", "_Text:te*t");
            Test("roam~", "_Text:roam~0.5");
            Test("roam~" + SnQuery.DefaultFuzzyValue.ToString(CultureInfo.InvariantCulture), "_Text:roam~0.5");
            Test("roam~0.8", "_Text:roam~0.8");
            Test("\"jakarta apache\"~10", "_Text:\"jakarta apache\"~10");
            Test("mod_date:[20020101 TO 20030101]");
            Test("title:{Aida TO Carmen}", "title:{aida TO carmen}");
            Test("jakarta apache", "_Text:jakarta _Text:apache");
            Test("jakarta^4 apache", "_Text:jakarta^4 _Text:apache");
            Test("\"jakarta apache\"^4 \"Apache Lucene\"", "_Text:\"jakarta apache\"^4 _Text:\"apache lucene\"");
            Test("\"jakarta apache\" jakarta", "_Text:\"jakarta apache\" _Text:jakarta");
            Test("\"jakarta apache\" OR jakarta", "_Text:\"jakarta apache\" _Text:jakarta");
            Test("\"jakarta apache\" AND \"Apache Lucene\"", "+_Text:\"jakarta apache\" +_Text:\"apache lucene\"");
            Test("+jakarta lucene", "+_Text:jakarta _Text:lucene");
            Test("\"jakarta apache\" NOT \"Apache Lucene\"", "_Text:\"jakarta apache\" -_Text:\"apache lucene\"");
            Test("NOT \"jakarta apache\"", "-_Text:\"jakarta apache\"");
            Test("\"jakarta apache\" -\"Apache Lucene\"", "_Text:\"jakarta apache\" -_Text:\"apache lucene\"");
            Test("(jakarta OR apache) AND website", "+(_Text:jakarta _Text:apache) +_Text:website");
            Test("title:(+return +\"pink panther\")", "+title:return +title:\"pink panther\"");
        }

        [TestMethod, TestCategory("IR")] // 11 tests
        public void Search_Compiler_Luc29__QueryTypes()
        {
            // ReSharper disable once JoinDeclarationAndInitializer
            Query q;
            q = Test("Name:aaaaa"); Assert.AreEqual(q.GetType(), typeof(TermQuery));
            q = Test("Id:1 Id:22"); Assert.AreEqual(q.GetType(), typeof(BooleanQuery));
            q = Test("Name:<aaaa"); Assert.AreEqual(q.GetType(), typeof(TermRangeQuery));
            q = Test("Name:a~0.8"); Assert.AreEqual(q.GetType(), typeof(FuzzyQuery));
            q = Test("Name:?aaaa"); Assert.AreEqual(q.GetType(), typeof(WildcardQuery));
            q = Test("Name:aa?aa"); Assert.AreEqual(q.GetType(), typeof(WildcardQuery));
            q = Test("Name:aaaa?"); Assert.AreEqual(q.GetType(), typeof(WildcardQuery));
            q = Test("Name:*aaaa"); Assert.AreEqual(q.GetType(), typeof(WildcardQuery));
            q = Test("Name:aa*aa"); Assert.AreEqual(q.GetType(), typeof(WildcardQuery));
            q = Test("Name:aa?a*"); Assert.AreEqual(q.GetType(), typeof(WildcardQuery));
            q = Test("Name:aaaa*"); Assert.AreEqual(q.GetType(), typeof(PrefixQuery));
            q = Test("Name:\"aaa bbb\""); Assert.AreEqual(q.GetType(), typeof(TermQuery)); // because Name uses KeywordAnalyzer
            q = Test("_Text:\"aa bbb\""); Assert.AreEqual(q.GetType(), typeof(PhraseQuery));
        }

        [TestMethod, TestCategory("IR")] // 4 tests
        public void Search_Compiler_Luc29__QueryType_Numeric()
        {
            // ReSharper disable once JoinDeclarationAndInitializer
            Query q;
            q = Test("Id:42"); Assert.AreEqual(q.GetType(), typeof(TermQuery));
            q = Test($"LongField1:{long.MaxValue}"); Assert.AreEqual(q.GetType(), typeof(TermQuery));
            q = Test("SingleField1:1.000001"); Assert.AreEqual(q.GetType(), typeof(TermQuery));
            q = Test("DoubleField1:1.0000001"); Assert.AreEqual(q.GetType(), typeof(TermQuery));
        }

        [TestMethod, TestCategory("IR")] // 20 tests
        public void Search_Compiler_Luc29__CqlExtension_Ranges()
        {
            // ReSharper disable once JoinDeclarationAndInitializer
            Query q;
            q = Test("Name:<aaa"); Assert.IsInstanceOfType(q, typeof(TermRangeQuery));
            q = Test("Name:>aaa"); Assert.IsInstanceOfType(q, typeof(TermRangeQuery));
            q = Test("Name:<=aaa"); Assert.IsInstanceOfType(q, typeof(TermRangeQuery));
            q = Test("Name:>=aaa"); Assert.IsInstanceOfType(q, typeof(TermRangeQuery));

            CheckNumericRange(Test("Id:<1000"), typeof(int));
            CheckNumericRange(Test("Id:>1000"), typeof(int));
            CheckNumericRange(Test("Id:<=1000"), typeof(int));
            CheckNumericRange(Test("Id:>=1000"), typeof(int));

            CheckNumericRange(Test("LongField1:<1000000"), typeof(long));
            CheckNumericRange(Test("LongField1:>1000000"), typeof(long));
            CheckNumericRange(Test("LongField1:<=1000000"), typeof(long));
            CheckNumericRange(Test("LongField1:>=1000000"), typeof(long));

            var value = 3.14.ToString(CultureInfo.InvariantCulture);
            CheckNumericRange(Test($"SingleField1:<{value}"), typeof(float));
            CheckNumericRange(Test($"SingleField1:>{value}"), typeof(float));
            CheckNumericRange(Test($"SingleField1:<={value}"), typeof(float));
            CheckNumericRange(Test($"SingleField1:>={value}"), typeof(float));

            value = 3.1415.ToString(CultureInfo.InvariantCulture);
            CheckNumericRange(Test($"DoubleField1:<{value}"), typeof(double));
            CheckNumericRange(Test($"DoubleField1:>{value}"), typeof(double));
            CheckNumericRange(Test($"DoubleField1:<={value}"), typeof(double));
            CheckNumericRange(Test($"DoubleField1:>={value}"), typeof(double));

            value = "2016-01-01 10:10:00";
            var ticks = DateTime.Parse(value).Ticks;
            CheckNumericRange(Test($"DateTimeField1:<'{value}'", $"DateTimeField1:<{ticks}"), typeof(long));
            CheckNumericRange(Test($"DateTimeField1:>'{value}'", $"DateTimeField1:>{ticks}"), typeof(long));
            CheckNumericRange(Test($"DateTimeField1:<='{value}'", $"DateTimeField1:<={ticks}"), typeof(long));
            CheckNumericRange(Test($"DateTimeField1:>='{value}'", $"DateTimeField1:>={ticks}"), typeof(long));
        }

        public Query Test(string queryText, string expected = null)
        {
            expected = expected ?? queryText;

            var indexingInfo = new Dictionary<string, IPerFieldIndexingInfo>
            {
                //{"_Text", new TestPerfieldIndexingInfoString()},
                {"#Field1", new TestPerfieldIndexingInfoString()},
                {"Field1", new TestPerfieldIndexingInfoString()},
                {"Field2", new TestPerfieldIndexingInfoString()},
                {"Field3", new TestPerfieldIndexingInfoString()},
                {"F1", new TestPerfieldIndexingInfoString()},
                {"F2", new TestPerfieldIndexingInfoString()},
                {"F3", new TestPerfieldIndexingInfoString()},
                {"F4", new TestPerfieldIndexingInfoString()},
                {"f1", new TestPerfieldIndexingInfoString()},
                {"f2", new TestPerfieldIndexingInfoString()},
                {"f3", new TestPerfieldIndexingInfoString()},
                {"f4", new TestPerfieldIndexingInfoString()},
                {"f5", new TestPerfieldIndexingInfoString()},
                {"f6", new TestPerfieldIndexingInfoString()},
                {"mod_date", new TestPerfieldIndexingInfoInt()},
                {"title", new TestPerfieldIndexingInfoString()},
                {"Name", new TestPerfieldIndexingInfoString()},
                {"Id", new TestPerfieldIndexingInfoInt()},
                {"LongField1", new TestPerfieldIndexingInfoLong()},
                {"SingleField1", new TestPerfieldIndexingInfoSingle()},
                {"DoubleField1", new TestPerfieldIndexingInfoDouble()},
                {"DateTimeField1", new TestPerfieldIndexingInfoDateTime()},
            };

            //using (new ContentRepository.Tests.Tools.RepositorySupportSwindler(new TestSearchEngineSupport(indexingInfo)))
            //using (SenseNet.Tests.Core.Tools.Swindle(typeof(SearchManager), "_searchEngineSupport", new TestSearchEngineSupport(indexingInfo)))
            //{
                var queryContext = new TestQueryContext(QuerySettings.Default, 0, indexingInfo);
                var parser = new CqlParser();
                var snQuery = parser.Parse(queryText, queryContext);

                var analyzers = indexingInfo.ToDictionary(kvp => kvp.Key, kvp => Lucene29LocalIndexingEngine.GetAnalyzer(kvp.Value));
                var indexFieldTypes = indexingInfo.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.IndexFieldHandler.IndexFieldType);

                // This is a non-functional object that's only purpose is 
                // to hold the indexing info for the visitor to work. 
                var sm = new LuceneSearchManager(new IndexDirectory());
                sm.SetIndexingInfo(analyzers, indexFieldTypes);

                var compiler = new Lucene29Compiler(sm.GetAnalyzer());
                var lucQuery = compiler.Compile(snQuery, queryContext);

                var lqVisitor = new LucQueryToStringVisitor(sm);
                lqVisitor.Visit(lucQuery.Query);
                var actual = lqVisitor.ToString();

                Assert.AreEqual(expected, actual);

                return lucQuery.Query;
            //}
        }

        private void CheckNumericRange(Query q, Type type)
        {
            var nq = q as NumericRangeQuery;
            Assert.IsNotNull(nq, $"The query is {q.GetType().Name} but {type.Name} expected.");
            //var val = (nq.GetMin() ?? nq.GetMax()).GetType();
            Assert.AreEqual(type, (nq.GetMin() ?? nq.GetMax()).GetType());
        }
    }
}
