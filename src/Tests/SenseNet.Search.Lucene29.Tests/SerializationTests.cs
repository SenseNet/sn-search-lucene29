using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SenseNet.Search.Indexing;
using SenseNet.Search.Querying;
using SenseNet.Search.Tests.Implementations;
using SenseNet.Testing;
using SenseNet.Tests.Core;
using SenseNet.Tests.Core.Implementations;

namespace SenseNet.Search.Lucene29.Tests
{
    [TestClass]
    public class SerializationTests : TestBase
    {
        [TestMethod]
        public void Serialization_SnTerm()
        {
            var snTerm = new SnTerm("Name", "Value");

            // ACT
            var serialized = snTerm.Serialize();
            var deserialized = SnTerm.Deserialize(serialized);

            // ASSERT
            var serialized2 = deserialized.Serialize();
            Assert.AreEqual(serialized, serialized2); // JSON
            Assert.IsTrue(serialized[0] == '{', "Not a JSON"); // JSON
        }

        [TestMethod]
        public void Serialization_DocumentUpdate()
        {
            var mode = IndexingMode.Analyzed;
            var storing = IndexStoringMode.No;
            var termVect = IndexTermVector.Default;
            // One sentence from the hungarian Lorem ipsum: http://www.lorumipse.hu/
            var stringData = "Lórum ipse védes, csingós, de a tasztergás kező, a csalártvány pedig egészen forózus kodás.";
            var fullTextData = stringData + "\r\n/root/mycontent\r\n123\r\n456\r\n789";

            var documentUpdate = new DocumentUpdate
            {
                UpdateTerm = new SnTerm("Name", "Value"),
                Document = new IndexDocument
                {
                    new IndexField("String1", stringData, mode, storing, termVect),
                    new IndexField("StringArray1", new[] {"A", "B", "C"}, mode, storing, termVect),
                    new IndexField("Bool1", true, mode, storing, termVect),
                    new IndexField("Int1", 42, mode, storing, termVect),
                    new IndexField("IntArray1", new[] {42, 43, 44}, mode, storing, termVect),
                    new IndexField("Long1", 142L, mode, storing, termVect),
                    new IndexField("Float1", 3.14F, mode, storing, termVect),
                    new IndexField("Double1", 3.14D, mode, storing, termVect),
                    new IndexField("DataTime", new DateTime(2024, 03, 15, 10, 11, 12), mode, storing, termVect),
                    new IndexField("_Text", fullTextData, mode, storing, termVect),
                }
            };

            // ACT
            var serialized = documentUpdate.Serialize();
            var deserialized = DocumentUpdate.Deserialize(serialized);

            // ASSERT
            var serialized2 = deserialized.Serialize();
            Assert.AreEqual(serialized, serialized2); // JSON
            Assert.IsTrue(serialized[0] == '{', "Not a JSON"); // JSON
        }

        [TestMethod]
        public void Serialization_IndexDocument()
        {
            var mode = IndexingMode.Analyzed;
            var storing = IndexStoringMode.No;
            var termVect = IndexTermVector.Default;
            // One sentence from the hungarian Lorem ipsum: http://www.lorumipse.hu/
            var stringData = "Lórum ipse védes, csingós, de a tasztergás kező, a csalártvány pedig egészen forózus kodás.";
            var fullTextData = stringData + "\r\n/root/mycontent\r\n123\r\n456\r\n789";
            var indexDocument = new IndexDocument
            {
                new IndexField("String1", stringData, mode, storing, termVect),
                new IndexField("StringArray1", new[] {"A", "B", "C"}, mode, storing, termVect),
                new IndexField("Bool1", true, mode, storing, termVect),
                new IndexField("Int1", 42, mode, storing, termVect),
                new IndexField("IntArray1", new[] {42, 43, 44}, mode, storing, termVect),
                new IndexField("Long1", 142L, mode, storing, termVect),
                new IndexField("Float1", 3.14F, mode, storing, termVect),
                new IndexField("Double1", 3.14D, mode, storing, termVect),
                new IndexField("DataTime", new DateTime(2024, 03, 15, 10, 11, 12), mode, storing, termVect),
                new IndexField("_Text", fullTextData, mode, storing, termVect),
            };

            // ACT
            var serialized = indexDocument.Serialize();
            var deserialized = IndexDocument.Deserialize(serialized);

            // ASSERT
            var serialized2 = deserialized.Serialize();
            Assert.AreEqual(serialized, serialized2); // JSON
            Assert.IsTrue(serialized[0] == '[', "Not a JSON"); // JSON
        }

        [TestMethod]
        public void Serialization_SnQuery()
        {
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

            // ACT-1: Keywords
            var queryText = "F1:V0 OR (F1:V1 AND F2:V2) .ALLVERSIONS .COUNTONLY .TOP:3 .SKIP:4 .SORT:F1 .REVERSESORT:F2 .AUTOFILTERS:OFF .LIFESPAN:ON .QUICK";
            var snQuery = SnQuery.Parse(queryText, new TestQueryContext(QuerySettings.Default, 1, indexingInfo));
            var serialized = Centralized.GrpcClient.Tools.SerializeSnQuery(snQuery);
            var deserialized = Centralized.GrpcService.Tools.DeserializeSnQuery(serialized);
            // ASSERT-1
            Assert.AreEqual(serialized, Centralized.GrpcClient.Tools.SerializeSnQuery(deserialized));

            // ACT-2: Ranges
            queryText = "Id:>100 Id:<10 Id:[ TO 10] Id:[10 TO ] Id:[10 TO 20] Id:{20 TO 30] Id:[30 TO 40} Id:{40 TO 50}";
            snQuery = SnQuery.Parse(queryText, new TestQueryContext(QuerySettings.Default, 1, indexingInfo));
            serialized = Centralized.GrpcClient.Tools.SerializeSnQuery(snQuery);
            deserialized = Centralized.GrpcService.Tools.DeserializeSnQuery(serialized);
            // ASSERT-2
            Assert.AreEqual(serialized, Centralized.GrpcClient.Tools.SerializeSnQuery(deserialized));

            // ACT-3: Projection
            queryText = "F1:V0 AND NOT F2:V2 .SELECT:Name";
            snQuery = SnQuery.Parse(queryText, new TestQueryContext(QuerySettings.Default, 1, indexingInfo));
            serialized = Centralized.GrpcClient.Tools.SerializeSnQuery(snQuery);
            deserialized = Centralized.GrpcService.Tools.DeserializeSnQuery(serialized);
            // ASSERT-3
            Assert.AreEqual(serialized, Centralized.GrpcClient.Tools.SerializeSnQuery(deserialized));
        }

    }
}
