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
using SenseNet.Testing;
using SenseNet.Tests.Core;

namespace SenseNet.Search.Lucene29.Tests
{
    [TestClass]
    public class SerializationTests : TestBase
    {
        [TestMethod]
        public void Serialization_IndexDocument()
        {
            var mode = IndexingMode.NotAnalyzed;
            var storing = IndexStoringMode.No;
            var termVect = IndexTermVector.Default;
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
            // /* 1 */
            //var serialized = Centralized.GrpcClient.Tools.Serialize(indexDocument);
            //var deserialized = Centralized.GrpcService.Tools.Deserialize<IndexDocument>(serialized);
            /* 2 */
            var serialized = indexDocument.Serialize();
            var deserialized = IndexDocument.Deserialize(serialized);

            // ASSERT
            var expected = new StringBuilder();
            foreach (var item in indexDocument)
            {
                if (item.Type == IndexValueType.StringArray)
                    continue;
                expected.Append(item).AppendLine();
            }

            var actual = new StringBuilder();
            foreach (var item in deserialized)
            {
                if (item.Type == IndexValueType.StringArray)
                    continue;
                actual.Append(item).AppendLine();
            }
            Assert.AreEqual(expected.ToString(), actual.ToString());
        }
    }
}
