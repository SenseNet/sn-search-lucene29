using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Fields;
using SenseNet.ContentRepository.Schema;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;
using Task = System.Threading.Tasks.Task;

namespace SenseNet.Search.Lucene29.Tests
{
    [TestClass]
    public class Lucene29SaveIndexTests : L29TestBase
    {
        [TestMethod, TestCategory("IR")]
        public async Task L29_SaveIndex_GetIndexProperties()
        {
            await L29Test(async () =>
            {
                var indexingEngine = Providers.Instance.SearchEngine.IndexingEngine;
                var allVersionIdsFromDb = Content.All
                    .DisableAutofilters()
                    .Where(c => c.InTree("/Root"))
                    .AsEnumerable()
                    .Select(c => c.ContentHandler.VersionId)
                    .OrderBy(x => x)
                    .ToArray();

                // ACTION
                var indexProperties = indexingEngine.GetIndexProperties();
                // 0, 256, 384


                // ASSERT
                // 1 - check serializability
                var sb = new StringBuilder();
                using (var writer = new StringWriter(sb))
                    JsonSerializer.Create(new JsonSerializerSettings
                            {Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore})
                        .Serialize(writer, indexProperties);
                Assert.IsTrue(sb.Length > 1000);

                // 2 - check versionIds
                var doc = indexingEngine.GetIndexDocumentByDocumentId(2);

                AssertSequenceEqual(allVersionIdsFromDb, indexProperties.VersionIds);

                // 3 - check field existence: indexed fields in contenttypes vs all fields in index
                var allChoiceSortFieldNames = ContentType.GetContentTypes()
                    .SelectMany(x => x.FieldSettings)
                    .Where(x => x is ChoiceFieldSetting)
                    .Select(x => x.Name + "_sort")
                    .Distinct()
                    .ToArray();
                // get all indexed fields + additional fields + choice _sort fields
                var allIndexedFields = ContentTypeManager.GetAllFieldNames(false)
                    .Union(new[]
                    {
                        "_Text", "IsInherited", "IsLastDraft", "IsLastPublic", "IsMajor", "IsPublic", "NodeTimestamp",
                        "VersionTimestamp"
                    })
                    .Union(allChoiceSortFieldNames)
                    .OrderBy(x => x)
                    .ToArray();
                // get field names from index
                var fieldsInIndex = indexProperties.FieldInfo
                    .Select(x => x.Key)
                    .OrderBy(x => x)
                    .ToArray();
                Assert.IsTrue(fieldsInIndex.Length > 100);
                // only fields occurring in existing content are indexed
                // (count of available fields is greater than the count of indexed fields).
                AssertSequenceEqual(Array.Empty<string>(), fieldsInIndex.Except(allIndexedFields).ToArray());

            });
        }
        [TestMethod, TestCategory("IR")]
        public async Task L29_SaveIndex_GetIndex()
        {
            await L29Test(async () =>
            {
                var indexingEngine = Providers.Instance.SearchEngine.IndexingEngine;

                // ACTION
                var invertedIndex = indexingEngine.GetInvertedIndex();

                // ASSERT
                // 1 - check serializability
                var sb = new StringBuilder();
                using (var writer = new StringWriter(sb))
                    JsonSerializer.Create(new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore })
                        .Serialize(writer, invertedIndex);
                Assert.IsTrue(sb.Length > 1000);
            }).ConfigureAwait(false);
        }
        [TestMethod, TestCategory("IR")]
        public async Task L29_SaveIndex_GetIndexByField()
        {
            await L29Test(async () =>
            {
                var indexingEngine = Providers.Instance.SearchEngine.IndexingEngine;

                // ACTION
                var invertedIndex = indexingEngine.GetInvertedIndex("LoginName");

                // ASSERT
                var loginNames = Content.All
                    .Where(c => c.TypeIs("User"))
                    .AsEnumerable()
                    .Select(c => ((string)c["LoginName"]).ToLowerInvariant())
                    .OrderBy(x => x)
                    .ToArray();
                var terms = invertedIndex.Keys
                    .OrderBy(x => x)
                    .ToArray();
                AssertSequenceEqual(loginNames, terms);

            }).ConfigureAwait(false);
        }
        [TestMethod, TestCategory("IR")]
        public async Task L29_SaveIndex_GetIndexDocByDocId()
        {
            await L29Test(async () =>
            {
                var indexingEngine = Providers.Instance.SearchEngine.IndexingEngine;
                var content = Content.All.First(c => c.TypeIs("User") && (string)c["LoginName"] == "VirtualADUser");

                // ACTION
                var invertedIndex = indexingEngine.GetInvertedIndex("LoginName");
                var docId = invertedIndex["virtualaduser"].First();
                var doc = indexingEngine.GetIndexDocumentByDocumentId(docId);

                // ASSERT
                Assert.AreEqual(content.Id.ToString(), doc["Id"]);
                Assert.AreEqual(content.ContentHandler.VersionId.ToString(), doc["VersionId"]);

            }).ConfigureAwait(false);
        }
        [TestMethod, TestCategory("IR")]
        public async Task L29_SaveIndex_GetIndexDocByVersionId()
        {
            await L29Test(async () =>
            {
                var indexingEngine = Providers.Instance.SearchEngine.IndexingEngine;
                var content = Content.All.First(c => c.TypeIs("User") && (string)c["LoginName"] == "VirtualADUser");
                var versionId = content.ContentHandler.VersionId;

                // ACTION
                var doc = indexingEngine.GetIndexDocumentByVersionId(versionId);

                // ASSERT
                Assert.AreEqual(content.Id.ToString(), doc["Id"]);
                Assert.AreEqual(content.ContentHandler.VersionId.ToString(), doc["VersionId"]);

            }).ConfigureAwait(false);
        }
    }
}
