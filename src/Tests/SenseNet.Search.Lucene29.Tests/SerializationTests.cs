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
using SenseNet.Testing;
using SenseNet.Tests.Core;

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
    }
}
