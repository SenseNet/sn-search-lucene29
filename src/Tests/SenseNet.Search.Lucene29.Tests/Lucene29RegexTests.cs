using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Contrib.Regex;
using Lucene.Net.Analysis;
using Lucene.Net.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Search;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Index.Configuration;
using SenseNet.Search.Querying;
using SenseNet.Search.Querying.Parser;
using SenseNet.Search.Querying.Parser.Predicates;
using SenseNet.Search.Tests.Implementations;
using SenseNet.Tests.Core;
using SenseNet.Tests.Core.Implementations;
using Task = System.Threading.Tasks.Task;

namespace SenseNet.Search.Lucene29.Tests
{
    [TestClass]
    public class Lucene29RegexTests : L29TestBase
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

        [TestMethod, TestCategory("IR")] // 1 tests
        public void L29_Compiler_Regex()
        {
            CompilerTest(@"[a-zA-Z]{3}\S{1}\w*");
        }
        [TestMethod, TestCategory("IR")] // 1 tests
        public void L29_Compiler_Regex_email()
        {
            // Original: Binary:'/(?i)(?<=^|[^a-z0-9!#$%\&'*+\/=?\^_`{|}~-])[a-z0-9!#$%\&'*+\/=?\^_`{|}~-]{1,256}(?:\.[a-z0-9!#$%\&'*+\/=?\^_`{|}~-]{1,256}){0,256}@(?:[a-z0-9](?:[a-z0-9-]{0,256}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,256}[a-z0-9])?(?=$|[^a-z0-9])/'
            CompilerTest(@"(?i)(?<=^|[^a-z0-9!#$%\&'*+\/=?\^_`{|}~-])[a-z0-9!#$%\&'*+\/=?\^_`{|}~-]{1,256}(?:\.[a-z0-9!#$%\&'*+\/=?\^_`{|}~-]{1,256}){0,256}@(?:[a-z0-9](?:[a-z0-9-]{0,256}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,256}[a-z0-9])?(?=$|[^a-z0-9])");
        }

        private void CompilerTest(string regex)
        {
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


        [TestMethod, TestCategory("IR")] // 7 tests
        public void Luc29_Compiler_Regex_ToString()
        {
            // ReSharper disable once JoinDeclarationAndInitializer
            Query q;
            q = RegexQueryTest("Name:/abc/", "Name:\"/abc/\""); Assert.IsTrue(q is RegexQuery);
            q = RegexQueryTest("Name:'/abc/'", "Name:\"/abc/\""); Assert.IsTrue(q is RegexQuery);
            q = RegexQueryTest("Name:\"/abc/\"", "Name:\"/abc/\""); Assert.IsTrue(q is RegexQuery);
            q = RegexQueryTest("Name:\"/[\\\\W]{1,2}/\""); Assert.IsTrue(q is RegexQuery);

            q = RegexQueryTest("Name:\"/abc\\\\w\\\\W/\"", @"Name:""/abc\\w\\W/"""); Assert.IsTrue(q is RegexQuery);
            q = RegexQueryTest("Name:\"/abc\\\"\\\\w\\\\W/\"", @"Name:""/abc""\\w\\W/"""); Assert.IsTrue(q is RegexQuery);
            q = RegexQueryTest("Name:'/abc\\\'\\\\w\\\\W/'", @"Name:""/abc'\\w\\W/"""); Assert.IsTrue(q is RegexQuery);
        }
        [TestMethod, TestCategory("IR")] // 1 tests
        public void Luc29_Compiler_Regex_ToString_email()
        {
            // Original: Binary:'/(?i)(?<=^|[^a-z0-9!#$%\&'*+\/=?\^_`{|}~-])[a-z0-9!#$%\&'*+\/=?\^_`{|}~-]{1,256}(?:\.[a-z0-9!#$%\&'*+\/=?\^_`{|}~-]{1,256}){0,256}@(?:[a-z0-9](?:[a-z0-9-]{0,256}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,256}[a-z0-9])?(?=$|[^a-z0-9])/'

            // ReSharper disable once JoinDeclarationAndInitializer
            Query q;
            q = RegexQueryTest(
                @"Name:""/(?i)(?<=^|[^a-z0-9!#$%\&'*+\/=?\^_`{|}~-])[a-z0-9!#$%\&'*+\/=?\^_`{|}~-]{1,256}(?:\.[a-z0-9!#$%\&'*+\/=?\^_`{|}~-]{1,256}){0,256}@(?:[a-z0-9](?:[a-z0-9-]{0,256}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,256}[a-z0-9])?(?=$|[^a-z0-9])/""",
                @"Name:""/(?i)(?<=^|[^a-z0-9!#$%&'*+/=?^_`{|}~-])[a-z0-9!#$%&'*+/=?^_`{|}~-]{1,256}(?:.[a-z0-9!#$%&'*+/=?^_`{|}~-]{1,256}){0,256}@(?:[a-z0-9](?:[a-z0-9-]{0,256}[a-z0-9])?.)+[a-z0-9](?:[a-z0-9-]{0,256}[a-z0-9])?(?=$|[^a-z0-9])/""");
            Assert.IsTrue(q is RegexQuery);
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


        private class TestCase
        {
            public string Name { get; set; }
            public string Pattern { get; set; }
            public string[] PositiveData { get; set; }
            public string[] NegativeData { get; set; }
        }
        private readonly TestCase[] _testCases = new[]
        {
            new TestCase
            {
                Name = "Major Credit Cards",
                Pattern = @"\b(\d{4}[ -]?\d{4}[ -]?\d{5}|\d{4}[ -]?\d{6}[ -]?\d{4}|\d{4}[ -]?\d{7}[ -]?\d{4}|\d{4}[ -]?\d{6}[ -]?\d{5}|\d{4}([ -]?\d{4}){3}|(\d{17,19}))\b",
                PositiveData = new[]
                {
                    "8195067115-45429",
                    "8625801397769",
                    "15671247885152575",
                    "7311-0013931-4892",
                    "4137-260004770583",
                    "3716 340205-8115",
                    "6040-54646979776",
                    "1233028412-35157",
                    "47693195837486450",
                    "27729202462-8975",
                },
                NegativeData = new[]
                {
                    "8195a67115-45429",
                    "862580x397769"
                },
            },
            new TestCase
            {
                Name = "Dates, Europe",
                // orig = @"\b(19|20)?[0-9]{2}[- /.](0?[1-9]|1[012])[- /.](0?[1-9]|[12][0-9]|3[01])\b",
                Pattern = @"\b(19|20)?[0-9]{2}[- \/.](0?[1-9]|1[012])[- \/.](0?[1-9]|[12][0-9]|3[01])\b",
                PositiveData = new[]
                {
                    "1984.09.3",
                    "1922/08-24",
                    "89 11.30",
                    "1914 10 30",
                    "32 4.30",
                    "53/09/23",
                    "47/2-31",
                    "2080/11.8",
                    "1900 7.27",
                    "07/11-31",
                },
                NegativeData = new[]
                {
                    "10/().36",
                    "10+31 1917",
                },
            },
            new TestCase
            {
                Name = "Dates, North America",
                // orig = @"\b(0?[1-9]|1[012])[- /.](0?[1-9]|[12][0-9]|3[01])[- /.](19|20)?[0-9]{2}\b",
                Pattern = @"\b(0?[1-9]|1[012])[- \/.](0?[1-9]|[12][0-9]|3[01])[- \/.](19|20)?[0-9]{2}\b",
                PositiveData = new[]
                {
                    "03 30 28",
                    "11-24-75",
                    "11 21-2069",
                    "12.1-10",
                    "11-09-1974",
                    "12.09/74",
                    "07/30/2057",
                    "4.16 11",
                    "12/24/94",
                    "03.31.57",
                },
                NegativeData = new[]
                {
                    "12,09/74",
                    "03,31,57",
                },
            },
            new TestCase
            {
                Name = "Email addresses",
                // orig = @"(?i)(?<=^|[^a-z0-9!#$%\&'*+/=?\^_`{|}~-])[a-z0-9!#$%\&'*+/=?\^_`{|}~-]{1,256}(?:\.[a-z0-9!#$%\&'*+/=?\^_`{|}~-]{1,256}){0,256}@(?:[a-z0-9](?:[a-z0-9-]{0,256}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,256}[a-z0-9])?(?=$|[^a-z0-9])",
                Pattern = @"(?<=^|[^a-z0-9!#$%\&'*+\/=?\^_`{|}~-])[a-z0-9!#$%\&'*+\/=?\^_`{|}~-]{1,256}(?:\.[a-z0-9!#$%\&'*+\/=?\^_`{|}~-]{1,256}){0,256}@(?:[a-z0-9](?:[a-z0-9-]{0,256}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,256}[a-z0-9])?(?=$|[^a-z0-9])",
                PositiveData = new[]
                {
                    "john.smith@email.com",
                    "embedded john.smith@email.com in sentence",
                    "a@b.cd",
                },
                NegativeData = new[]
                {
                    "john.smith@@email.com",
                    "john.smith@email,com",
                },
            },
            new TestCase
            {
                Name = "IPv4 Address",
                Pattern = @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b",
                PositiveData = new[]
                {
                    "229.0.19.246",
                    "embedded 03.214.20.253 in sentence",
                    "249.216.208.250",
                },
                NegativeData = new[]
                {
                    "229.0.19-246",
                    "229.0,19.246",
                },
            },
            new TestCase
            {
                Name = "NANP Phone Numbers",
                Pattern = @"\(?\b[0-9]{3}\)?[-. ]?[0-9]{3}[-. ]?[0-9]{4}\b",
                PositiveData = new[]
                {
                    "(036.737 8343",
                    "(315) 577.2707",
                    "968)238-6773",
                    "256 080-8486",
                    "(000)869-5922",
                    "796.806.6949",
                    "(707332-0335",
                    "269)877 8947",
                    "282)2821757",
                    "662-7231319",
                },
                NegativeData = new[]
                {
                    "(315} 577.2707",
                    "256 080=8486",
                },
            },
            //new TestCase
            //{
            //    Name = "UNC paths",
            //    // orig = @"(?i)(\b[^/:*\?"<>|\r\n\t\x00-\x1F]:|\\\\[a-z0-9]+)([file://[%5e/:*/%3f%22%3c%3e|/r/n/t/x00-/x1F%5d%7b0,256%7d)%7b1,256%7d]\\[^/:*\?"<>|\r\n\t\x00-\x1F]{0,256}){1,256}",
            //    Pattern = @"",
            //    PositiveData = new[] {"", "",},
            //    NegativeData = new[] {"", "",},
            //},
            new TestCase
            {
                Name = "Common Uniform Resource Identifiers (URLs)",
                Pattern = @"\b(?i)(file|gopher|news|nntp|telnet|ftps?|https?|sftp|ldaps?):\/\/([-A-Z0-9.]+)(:[0-9]+)?(\/[-A-Z0-9+\&@#$\/%=~_|!:,.;()'*]*)?(\?[-A-Z0-9+\&@#$\/%=~_|!:,.;()'*]*)?",
                PositiveData = new[]
                {
                    "http://S4KF-4GZMW-0YH91MYW:5702/MDL$SG!*400H/WL|Q#W=3T*MLZ4F5)4ZNWB?(1|XLOSDWX~QT;#-R(-2P.38-J1(9F8YPW|C*U)QID/4HSFQ-(2",
                    "https://3XO-MVS2.J2CP.RX-5F4:30015399126857445/G-VW4'S_.*FF5,59)C6B;7~ORU+E$:UTI?64%#JQ;6D%ER@W7!&=%LUTW,M,)#JHO;JGV.|6S%N~G2N-!X6A~;58P",
                    "telnet://ECK6X5SS.COE35KDAFO4IK85:9945?T;L)SF,%",
                    "ldaps://PJEOQ-8GCEN:58/DLD#UU3@E:A'.KE!'K8$A,SPU/2B-O2AF/_W~,",
                    "news://Q4TI0ZXX9.YDEFGY-42UKQG-4E9RGR:3364",
                    "ftps://93GG6N1KBKZ.M13CZ7-5S1R--9TB5-8PKRRNP.GCMRGJ",
                    "news://YGYXGX9QWP-HVBY2-IGA6Y--ME3N6-TWKAWH-T-97YB",
                    "nntp://4NU277I?VR+X#/U6G-E.~|!)W&HAU3..,RZAN6+H*!@|3DEVX",
                    "file://3DNK9CO-PQKEL.HBZ.IFGM1Z:0385879/IXU!IEPU,QU!8L|C|Q=4@%+SQ$~%B8S|YL5!W+SYMKSX'N*'67W7@YA*6/;:.91(?&5#),CKV*YS.=FR",
                    "gopher://7W4WKEL491-.VHP39PQ8G1PNWVKF2DCQHXOPJIZTJQMTE74XD2TSILO:841586602127901485589822460652?A,9W,JSDPSX3-=@#&CP3M3U=MUMTA@JG0=G**XF0.,~-F$6APF1,344@5/1211HW$(PH/4%%-TK9",
                },
                NegativeData = new[]
                {
                    "http:/example.com",
                    "https//server.com",
                },
            },
            new TestCase
            {
                Name = "Lithuania: Personal code (Asmens kodas)",
                Pattern = @"\b[3-6]\d{2}(0[1-9]|1[012])(0[1-9]|[12]\d|3[01])\d{4}\b",
                PositiveData = new[]
                {
                    "65102295240",
                    "39611233880",
                    "46107141834",
                    "44401090453",
                    "46911088209",
                    "44311305133",
                    "56611312025",
                    "39511026075",
                    "63310158550",
                    "42210259716",
                },
                NegativeData = new[]
                {
                    "05102295240",
                    "39691233880",
                    "46107a41834",
                    "44401090453x",
                },
            },
            new TestCase
            {
                Name = "Austria: Social insurance number",
                Pattern = @"\b[0-9]{10}\b",
                PositiveData = new[]
                {
                    "2390004364",
                    "0096547237",
                    "5923976140",
                },
                NegativeData = new[]
                {
                    "239000436",
                    "23900043645",
                    "239xxx4364",
                    ".390004364",
                },
            },
            new TestCase
            {
                Name = "Switzerland: Old AVS format with personal data encoded",
                Pattern = @"\b[0-9]{3}\.?[0-9]{2}\.?[0-9]{3}\.?[0-9]{3}\b",
                PositiveData = new[]
                {
                    "20101.633988",
                    "54332043.248",
                    "082.74.366.528",
                    "045.00364.007",
                    "953.11242.965",
                    "640.78817345",
                    "212.27.949368",
                    "616.59.448.039",
                    "79771.823.367",
                    "958.92.345.199",
                },
                NegativeData = new[]
                {
                    "20101,633988",
                    "9580.92.345.199",
                    "958.920.345.199",
                    "958.92.3450.199",
                    "958.92.345.1990",
                },
            },
            new TestCase
            {
                Name = "Switzerland: New AVS format (16 digits with constant prefix 756, which is ISO 3166-1 country code)",
                Pattern = @"\b756\.?[0-9]{4}\.?[0-9]{4}\.?[0-9]{2}\b",
                PositiveData = new[]
                {
                    "7566249.9987.58",
                    "756.04463171.62",
                    "756.2893291779",
                    "7569207.707402",
                    "756.9798408430",
                },
                NegativeData = new[]
                {
                    "755.4714383111",
                    "756624.9987.58",
                    "7566249.998.58",
                    "7566249.9987.5",
                    "7566249,9987,58",
                },
            },
            new TestCase
            {
                Name = "Belgium: Identification number of the National Register.",
                Pattern = @"\b\d{2}[.]?(0[1-9]|1[012])[.]?(0[1-9]|[12]\d|3[01])-?\d{3}[.]?\d{2}\b",
                PositiveData = new[]
                {
                    "74.12.02-396.95",
                    "7412.02-396.95",
                    "48.121079372",
                    "1104.1605664",
                    "71.073137332",
                    "3301.06308.80",
                    "441106-687.31",
                    "191115013.13",
                    "31.023194611",
                    "331006-917.47",
                    "03.10.0757275",
                },
                NegativeData = new[]
                {
                    "7412,02-396.95",
                    "740.12.02-396.95",
                    "74.120.02-396.95",
                    "74.12.020-396.95",
                    "74.12.02-3960.95",
                    "74.12.02-396.950",
                },
            },
            new TestCase
            {
                Name = "Netherlands: Burgerservicenummer, sofinummer (Citizen's Service Number).",
                Pattern = @"\b[0-9]{9}\b",
                PositiveData = new[]
                {
                    "296257485",
                    "797400776",
                    "100925516",
                },
                NegativeData = new[]
                {
                    "296xxx485",
                    "7974007760",
                    "10092551",
                },
            },
            new TestCase
            {
                Name = "Italy: Codice fiscale",
                // orig = @"\b[A-Z]{6}[0-9]{2}[A-E,H,L,M,P,R-T][0-9]{2}[A-Z0-9]{5}\b",
                Pattern = @"\b[a-z]{6}[0-9]{2}[a-e,h,l,m,p,r-t][0-9]{2}[a-z0-9]{5}\b",
                PositiveData = new[]
                {
                    "AAAAAA99M99X9X9X",
                    "FXJBXK56M23S8UJC",
                    "EOZUOH06T48HFMNY",
                    "YMZTPD31H13Z44AP",
                    "OYKACM93B06XKJIJ",
                    "AWKMBP36L969H4AG",
                    "GODDCZ07E52IZGUX",
                    "OJKEYV17A41Z0GE8",
                    "TBGGCU97R10NU4CK",
                    "CLQNUB33T66ST489",
                    "NXJIWW19P55RNUMN",
                },
                NegativeData = new[]
                {
                    "AAAAA099M99X9X9X",
                    "AAAAAAA9M99X9X9X",
                    "AAAAAA99Y99X9X9X",
                    "AAAAAA99MA9X9X9X",
                    "AAAAAA99M99X9X9",
                    "AAAAAA99M99X9X9XA",
                },
            },
            new TestCase
            {
                Name = "Romania: Nr personal",
                Pattern = @"\b[1-9]\d{2}(0[1-9]|1[012])(0[1-9]|[12]\d|3[01])\d{6}\b",
                PositiveData = new[]
                {
                    "8731104436939",
                    "3001122428835",
                    "4821111623795",
                    "5720909228457",
                    "7921213494506",
                    "8140425051987",
                    "9901231950930",
                    "4891131954000",
                    "1390115016264",
                    "3110128658165",
                },
                NegativeData = new[]
                {
                    "0731104436939",
                    "87311044369390",
                    "873110443693",
                },
            },
            new TestCase
            {
                Name = "Denmark: CPR-nummer (personnummer)",
                Pattern = @"\b(0[1-9]|[12]\d|3[01])(0[1-9]|1[012])\d{2}-?\d{4}\b",
                PositiveData = new[]
                {
                    "111185-9068",
                    "3111859068",
                    "1612971272",
                    "310470-3292",
                    "300661-1823",
                    "2303989156",
                    "3102456783",
                    "3112917083",
                    "120674-6595",
                    "210510-5476",
                    "0706860111",
                },
                NegativeData = new[]
                {
                    "1101185-9068",
                    "1111085-9068",
                    "1111850-9068",
                    "111185-90680",
                    "111185-906",
                    "a11185-9068",
                    "111185-906a",
                },
            },
            new TestCase
            {
                Name = "Spain: Documento Nacional de Identidad",
                // orig = @"\b[0-9,X,M,L,K,Y][0-9]{7}-?[A-Z]\b",
                Pattern = @"\b[0-9xmlky][0-9]{7}-?[a-z]\b",
                PositiveData = new[]
                {
                    "53363269Y",
                    "91793428T",
                    "M4854794Y",
                    "43574957Q",
                    "60597434-C",
                    "83480103-C",
                    "L1207880-P",
                    "87837206-T",
                    "18754517-Q",
                    "Y0217292-M",
                },
                NegativeData = new[]
                {
                    "53363269YX",
                    "53363269",
                    "Z3363269Y",
                    "533632690-Y",
                    "5336326-Y",
                    "53363269-0",
                },
            },
            new TestCase
            {
                Name = "Bulgaria: Uniform Civil Number (Bulgarian: Единен граждански номер)",
                Pattern = @"\b\d{2}([024][1-9]|[135][012])(0[1-9]|[12]\d|3[01])\d{4}\b",
                PositiveData = new[]
                {
                    "0029313243",
                    "7429313243",
                    "2532050653",
                    "3232225548",
                    "6541314470",
                    "3712316083",
                    "4852064815",
                    "5006075768",
                    "9150248260",
                    "7811308092",
                    "7450174283",
                },
                NegativeData = new[]
                {
                    "00293132430",
                    "002931324",
                },
            },
            new TestCase
            {
                Name = "Norway: Fødselsnummer",
                Pattern = @"\b(0[1-9]|[12]\d|3[01])[.]?([04][1-9]|[15][012])[.]?\d{2}[ ]?\d{5}\b",
                PositiveData = new[]
                {
                    "09.51.99 99999",
                    "03.5153 45916",
                    "085228 18588",
                    "01.01.4578791",
                    "304315 73903",
                    "15.5269 48214",
                    "05.11.6227304",
                    "09.12.05 07129",
                    "31461549107",
                    "31.101753363",
                    "02.03.1843453",
                },
                NegativeData = new[]
                {
                    "090.51.99 99999",
                    "09.510.99 99999",
                    "09.51.990 99999",
                    "09.51.99 099999",
                    "09.51.99-99999",
                },
            },
            new TestCase
            {
                Name = "Finland: Personal identity code (henkilötunnus)",
                // orig = @"\b(0[1-9]|[12]\d|3[01])[.]?(0[1-9]|1[012])[.]?\d{2}[+\-A]\d{3}[0-9A-Z]\b",
                Pattern = @"\b(0[1-9]|[12]\d|3[01])[.]?(0[1-9]|1[012])[.]?\d{2}[+\-a]\d{3}[0-9a-z]\b",
                PositiveData = new[]
                {
                    "300640-5264",
                    "31.03.33-308U",
                    "18.0683-3056",
                    "130616A196Q",
                    "240337+4983",
                    "30.1179+177U",
                    "03.1065-861L",
                    "0204.92+488T",
                    "310723+8215",
                    "030500-463O",
                },
                NegativeData = new[]
                {
                    "311.03.33-308U",
                    "31.031.33-308U",
                    "31.03.331-308U",
                    "31.03.33-3081U",
                    "31.03.33-308",
                },
            },
            //new TestCase
            //{
            //    Name = "Europe: ISO 13616 with ISO 3166 country code prefix",
            //    Pattern = @"\b[A-Z]{2}?[ ]?\d{2}[ ]?([0-9A-Z]{4}[ ]?){1,5}[0-9A-Z]{1,4}\b",
            //    PositiveData = new[] {"",},
            //    NegativeData = new[] {"",},
            //},
            new TestCase
            {
                Name = "Estonia: Isikukood (personal code)",
                Pattern = @"\b[1-6]\d{2}(0[1-9]|1[012])(0[1-9]|[12]\d|3[01])\d{4}\b",
                PositiveData = new[]
                {
                    "36006052980",
                    "16409097897",
                    "24002029560",
                    "64504060766",
                    "53109311774",
                    "30410118586",
                    "15208305716",
                    "69412046124",
                    "46010078837",
                    "12711278135",
                },
                NegativeData = new[]
                {
                    "00000000000",
                    "360060529800",
                    "3600605298",
                    "360XX6052980",
                },
            },
            new TestCase
            {
                Name = "UK: UK NHS Number",
                Pattern = @"\b[0-9]{3}[ -]?[0-9]{3}[ -]?[0-9]{4}\b",
                PositiveData = new[]
                {
                    "000-000-0000",
                    "405 5226042",
                    "719-8638126",
                    "174-9831235",
                    "3929787923",
                    "342 788-6370",
                    "9457429549",
                    "632-4160849",
                    "830-829 1796",
                    "3280444928",
                    "7922062662",
                },
                NegativeData = new[]
                {
                    "0000-000-0000",
                    "000-0000-0000",
                    "000-000-00000",
                    "AAA-000-0000",
                    "000-AAA-0000",
                    "000-000-AAAA",
                },
            },
            new TestCase
            {
                Name = "UK: National insurance number",
                // orig = @"(?i)\b(?!BG)(?!GB)(?!NK)(?!KN)(?!TN)(?!NT)(?!ZZ)(?:[A-CEGHJ-PR-TW-Z][A-CEGHJ-NPR-TW-Z])[ ]?[0-9]{2}[ ]?[0-9]{2}[ ]?[0-9]{2}[ ]?([A-DFMP]\b|[ ])",
                Pattern = @"(?i)\b(?!bg)(?!gb)(?!nk)(?!kn)(?!tn)(?!nt)(?!zz)(?:[a-ceghj-pr-tw-z][a-ceghj-npr-tw-z])[ ]?[0-9]{2}[ ]?[0-9]{2}[ ]?[0-9]{2}[ ]?([a-dfmp]\b|[ ])",
                PositiveData = new[]
                {
                    "SH 359550 ",
                    "OR769437A",
                    "AA 772882 ",
                    "YJ48 54 41 ",
                    "CH1753 49 ",
                    "LH 186157 ",
                    "YT18 9229 ",
                    "PK 11 53 88 M",
                    "SP 51 2775A",
                    "XM4430 35B",
                },
                NegativeData = new[]
                {
                    "SH 359550X ",
                    "XSH 359550 ",
                    "SH 359550",
                },
            },
            new TestCase
            {
                Name = "France: Social security number (INSEE)",
                // orig = @"\b[123478][ ]?\d{2}(0[1-9]|1[012])[ ]?(\d{5}|2[AB]\d{3})[ ]?\d{3}[ ]?\d{2}\b",
                Pattern = @"\b[123478][ ]?\d{2}(0[1-9]|1[012])[ ]?(\d{5}|2[ab]\d{3})[ ]?\d{3}[ ]?\d{2}\b",
                PositiveData = new[]
                {
                    "3 5510 2A856957 81",
                    "234122A767 78447",
                    "392122B352 391 12",
                    "1120628161294 97",
                    "16608 0554867841",
                    "49508 2B319240 54",
                    "3661034689 21705",
                    "7 13122A297 88846",
                    "4 91122B674 960 34",
                    "3 3001 2B905436 51",
                },
                NegativeData = new[]
                {
                    "03 5510 2A856957 81",
                    "5510 2A856957 81",
                },
            },
            new TestCase
            {
                Name = "Sweden: Personal id number",
                Pattern = @"\b\d{2}(0[1-9]|1[012])(0[1-9]|[12]\d|3[01])[-+]\d{4}\b",
                PositiveData = new[]
                {
                    "371231+5825",
                    "520230-3958",
                    "300831+0958",
                    "331002-1232",
                    "801121-6543",
                    "520730+9356",
                    "400107-6407",
                    "861226+6077",
                    "020903+6130",
                    "651003+5671",
                },
                NegativeData = new[]
                {
                    "371231+58250",
                    "371231+582",
                    "371231.5825"
                },
            },
            new TestCase
            {
                Name = "Poland: National identification number",
                Pattern = @"\b\d{2}(0[1-9]|1[012])(0[1-9]|[12]\d|3[01])\d{5}\b",
                PositiveData = new[]
                {
                    "65050497730",
                    "88120748680",
                    "37112961100",
                    "30093030886",
                    "03040313044",
                    "55072827573",
                    "25120155799",
                    "77102043349",
                    "08110532887",
                    "81101397794",
                },
                NegativeData = new[]
                {
                    "6505049773",
                    "650504977300",
                    "6505049773A",
                },
            },
            new TestCase
            {
                Name = "Germany: Personenkennziffer (Bundeswehr)",
                // orig = @"\b(0[1-9]|[12]\d|3[01])(0[1-9]|1[012])\d{2}-?[A-Z]-?\d{5}\b",
                Pattern = @"\b(0[1-9]|[12]\d|3[01])(0[1-9]|1[012])\d{2}-?[a-z]-?\d{5}\b",
                PositiveData = new[]
                {
                    "190765L99145",
                    "150709J32512",
                    "241110S99388",
                    "310307B-50241",
                    "310799I38429",
                    "281021-E-90266",
                    "310235W-33381",
                    "301181-E19221",
                    "300595Y-41869",
                    "300250L-57370",
                },
                NegativeData = new[]
                {
                    "281021-EE-90266",
                    "2810210-E-90266",
                    "28102-E-90266",
                    "281021-E-990266",
                    "281021-E-0266",
                },
            },
            new TestCase
            {
                Name = "Latvia: Personal no (Personas kodas)",
                Pattern = @"\b(0[1-9]|[12]\d|3[01])(0[1-9]|1[012])\d{2}-?[0-2]\d{4}\b",
                PositiveData = new[]
                {
                    "281276-13437",
                    "211064-29847",
                    "04121218319",
                    "17127402032",
                    "05101501390",
                    "22036022127",
                    "30115008380",
                    "300283-00783",
                    "071178-12213",
                    "13085327507",
                },
                NegativeData = new[]
                {
                    "0110765-13437",
                    "01107-13437",
                    "281276-3437",
                    "281276-113437",
                },
            },
            new TestCase
            {
                Name = "Ireland: Personal Public Service Number",
                // orig = @"\b[0-9]{7}[A-Z]W?\b",
                Pattern = @"\b[0-9]{7}[a-z]w?\b",
                PositiveData = new[]
                {
                    "0752356T",
                    "3095820GW",
                    "3401466HW",
                    "8523608S",
                    "8133956V",
                    "8646859Q",
                    "6432038V",
                    "3311040XW",
                    "4192879D",
                    "0784021J",
                },
                NegativeData = new[]
                {
                    "075256T",
                    "07529356T",
                    "0752356TWW",
                    "075235TW",
                },
            },
            new TestCase
            {
                Name = "Czech,Slovakia: Birth Number (Rodné číslo)",
                Pattern = @"\b\d{2}([05][1-9]|[16][012])(0[1-9]|[12]\d|3[01])/?\d{4}\b",
                PositiveData = new[]
                {
                    "651002/2308",
                    "9011257751",
                    "200301/7265",
                    "1312307166",
                    "6612305186",
                    "356229/7179",
                    "1512300443",
                    "4011300298",
                    "055216/0329",
                    "4258090666",
                },
                NegativeData = new[]
                {
                    "6510021/2308",
                    "651002//2308",
                    "65100/2308",
                    "651002/208",
                    "651002/22308",
                },
            },
            new TestCase
            {
                Name = "United States: Social Security Number",
                Pattern = @"\b([0-6]\d{2}|7[0-6]\d|77[0-2])(([ ]\d{2}[ ])|([\-]\d{2}[\-])|\d{2})(\d{4})\b",
                PositiveData = new[]
                {
                    "293-18-1660",
                    "750 14 8644",
                    "722453432",
                    "559 28 0446",
                    "746026129",
                    "708-88-8740",
                    "771651368",
                    "041 24 0111",
                    "766 57 0254",
                    "770 52 8041",
                },
                NegativeData = new[]
                {
                    "29A-18-1660",
                    "29-18-1660",
                    "2923-18-1660",
                    "293-8-1660",
                    "293-183-1660",
                    "293-18-660",
                    "293-18-31660",
                },
            },
            //new TestCase
            //{
            //    Name = "Austria: New national identificationnumber",
            //    Pattern = @"(?<=^|[^A-Za-z0-9+/=])[A-Za-z0-9+/]{22}([A-Za-z0-9+/]{4})?[A-Za-z0-9+/=]{2}(?=$|[^A-Za-z0-9+/=])",
            //    /* @"[A-Za-z0-9+/]{22}[A-Za-z0-9+/=][A-Za-z0-9+/=]";  https://ipsec.pl/european-personal-data-regexp-patterns.html */
            //    PositiveData = new[] {"",},
            //    NegativeData = new[] {"",},
            //},
            new TestCase
            {
                Name = "Germany: Steuer-Identifikationsnummer",
                Pattern = @"\b[0-9]{2}[ ]?[0-9]{3}[ ]?[0-9]{3}[ ]?[0-9]{3}\b",
                PositiveData = new[]
                {
                    "94 160 022 583",
                    "90 467 844400",
                    "51694 102928",
                    "12 100715805",
                    "70 203 972070",
                    "66 527814233",
                    "37208 511703",
                    "54 466 650 771",
                    "73 974532166",
                    "34357945770",
                },
                NegativeData = new[]
                {
                    "940 160 022 583",
                    "94 0160 022 583",
                    "94 160 0022 583",
                    "94 160 022 0583",
                    "9 160 022 583",
                    "94 10 022 583",
                    "94 160 02 583",
                    "94 160 022 53",
                },
            },
            new TestCase
            {
                Name = "Hungary: Personal identfication number (Személyi szám)",
                Pattern = @"\b[1-8][ ]?\d{2}([024][1-9]|[135][012])(0[1-9]|[12]\d|3[01])[ ]?\d{4}\b",
                PositiveData = new[]
                {
                    "59310307234",
                    "6105230 1387",
                    "8042630 1875",
                    "5 5349307780",
                    "2 310429 3250",
                    "5 7721308342",
                    "7 1430301762",
                    "6 890407 7350",
                    "2 9522306472",
                    "86030189816",
                },
                NegativeData = new[]
                {
                    "21 310429 3250",
                    "2 3104291 3250",
                    "2 310429 32501",
                },
            },
            new TestCase
            {
                Name = "Hungary: Social insurance number (TAJ szám)",
                Pattern = @"\b[0-9]{3}[ ]?[0-9]{3}[ ]?[0-9]{3}\b",
                PositiveData = new[]
                {
                    "167 712314",
                    "147531946",
                    "825746 081",
                    "119642812",
                    "297675 182",
                    "753918 900",
                    "011502636",
                    "740362 656",
                    "552 960 075",
                    "526 567955",
                },
                NegativeData = new[]
                {
                    "5521 96 075",
                    "55 9601 075",
                    "552 96 0751",
                    "552 960 07",
                },
            },
            new TestCase
            {
                Name = "Greece: Tautotita",
                // orig = @"\b([A-Z]|[ABEZHIKMNOPTYX]{2})-?\d{6}\b",
                Pattern = @"\b([a-z]|[abezhikmnoptyx]{2})-?\d{6}\b",
                PositiveData = new[]
                {
                    "M890336",
                    "NM-000243",
                    "EB-674693",
                    "YZ544527",
                    "YP-023089",
                    "KY-399369",
                    "V333690",
                    "ZY-112338",
                    "BY-559146",
                    "NN886542",
                },
                NegativeData = new[]
                {
                    "NC886542",
                    "NN86542",
                    "NN9886542",
                },
            },
            new TestCase
            {
                Name = "Germany: Versicherungsnummer, Rentenversicherungsnummer",
                // orig = @"\b\d{2}(0[1-9]|[12]\d|3[01])(0[1-9]|1[012])\d{2}[A-Z]\d{3}\b",
                Pattern = @"\b\d{2}(0[1-9]|[12]\d|3[01])(0[1-9]|1[012])\d{2}[a-z]\d{3}\b",
                PositiveData = new[]
                {
                    "67031124M768",
                    "63041295S924",
                    "22041252X792",
                    "17310367N924",
                    "83070667U677",
                    "74291014X050",
                    "88010247W941",
                    "07281099C096",
                    "25111014R539",
                    "16040275S525",
                },
                NegativeData = new[]
                {
                    "6703112MM768",
                    "67031124M68",
                    "670311294M768",
                },
            },
            new TestCase
            {
                Name = "Austria: National identification number - Zentrales Melderegister (Central Register of Residents - CRR)",
                Pattern = @"\b[0-9]{12}\b",
                PositiveData = new[]
                {
                    "826996225284",
                    "263080997040",
                    "486537853333",
                    "561384710596",
                    "456528237164",
                    "486954854403",
                    "184190978564",
                    "119419324951",
                    "493541177390",
                    "052677454863",
                },
                NegativeData = new[]
                {
                    "82699622528",
                    "82699622528X",
                    "8269962252834",
                },
            },
            new TestCase
            {
                Name = "Czech,Slovakia: Citizen's Identification Card Number (Číslo občianskeho preukazu)",
                // orig = @"\b[A-Z]{2}[0-9]{6}\b",
                Pattern = @"\b[a-z]{2}[0-9]{6}\b",
                PositiveData = new[]
                {
                    "MA070506",
                    "KZ937619",
                    "GJ861860",
                    "YC989349",
                    "RG509388",
                    "YX205320",
                    "VA714001",
                    "ZA317368",
                    "IN878209",
                    "PE808148",
                },
                NegativeData = new[]
                {
                    "MAX70506",
                    "M0070506",
                    "MA00506",
                    "MA0970506",
                },
            },
        };

        [TestMethod, TestCategory("IR")] // 1 tests
        public async Task Luc29_Exec_Regex()
        {
            await L29Test(builder =>
            {
                var x = 0;
            }, async () =>
            {
                var index = 0;
                foreach (var testCase in _testCases)
                {
                    Debug.WriteLine(">>>> TestCase {0}/{1}: {2}", ++index, _testCases.Length, testCase.Name);

                    var folderName = "RegexTestCases" + index;
                    CreateContentsForTestCase(testCase, folderName);

                    var queryText = GetQueryText("DisplayName", testCase.Pattern);
                    queryText = $"+InFolder:/Root/{folderName} +{queryText}";
                    //var result = ContentQuery.Query(queryText, QuerySettings.AdminSettings);
                    var result = CreateSafeContentQuery(queryText, QuerySettings.AdminSettings).Execute();
                    var actual = result.Nodes.Select(n => n.DisplayName).OrderBy(s => s).ToArray();
                    var expected = testCase.PositiveData.OrderBy(x => x).ToArray();
                    var actualJoined = string.Join(" ", actual);
                    var expectedJoined = string.Join(" ", expected);
                    Assert.AreEqual(expectedJoined, actualJoined,
                        $"TestCase '{testCase.Name}' failed.");
                }
            });
        }
        private void CreateContentsForTestCase(TestCase testCase, string folderName)
        {
            var root = Content.Load("/Root/" + folderName);
            if (root != null)
                return;

            root = Content.CreateNew("SystemFolder", Repository.Root, folderName);
            root.Save();

            var displayNames = testCase.PositiveData.Union(testCase.NegativeData);

            var index = 0;
            foreach (var displayName in displayNames)
            {
                var content = Content.CreateNew("SystemFolder", root.ContentHandler, $"Content{++index:##}");
                content.DisplayName = displayName;
                content.Save();
            }
        }
        private string GetQueryText(string fieldName, string regularExpression)
        {
            var regexForQuery = regularExpression
                .Replace(@"\", @"\\")
                .Replace("\"", "\\\"");
            return $"{fieldName}:\"/{regexForQuery}/\"";
        }

    }
}
