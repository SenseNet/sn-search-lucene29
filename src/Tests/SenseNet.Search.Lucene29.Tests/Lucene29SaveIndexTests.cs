using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
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

        [TestMethod, TestCategory("IR")]
        public void L29_SaveIndex_ForwardOnlyDictionary()
        {
            var data = new Dictionary<string, IDictionary<string, List<int>>>
            {
                {"eee", new Dictionary<string, List<int>>
                {
                    {"aa", new List<int>{1,2,3}},
                }},
                {"ddd", new Dictionary<string, List<int>>
                {
                    {"bb", new List<int>{4,5,6}}, {"aa", new List<int>{1,2,3}},
                }},
                {"ccc", new Dictionary<string, List<int>>
                {
                    {"cc", new List<int>{7,8,9}}, {"bb", new List<int>{4,5,6}}, {"aa", new List<int>{1,2,3}},
                }},
                {"bbb", new Dictionary<string, List<int>>
                {
                    {"dd", new List<int>{1,3,5}}, {"cc", new List<int>{7,8,9}}, {"bb", new List<int>{4,5,6}}, {"aa", new List<int>{1,2,3}},
                }},
                {"aaa", new Dictionary<string, List<int>>
                {
                    {"ee", new List<int>{2,4,6}}, {"dd", new List<int>{1,3,5}}, {"cc", new List<int>{7,8,9}}, {"bb", new List<int>{4,5,6}}, {"aa", new List<int>{1,2,3}},
                }},
            };

            IDictionary<string, List<int>> GetFieldData(ForwardOnlyDictionaryState state, string field)
            {
                var keys = data[field].Keys;
                var subState = new ForwardOnlyDictionaryState {FieldName = field, IndexReader = state?.IndexReader};
                return new ForwardOnlyDictionary<string, List<int>>(subState, keys, GetTermData);
            }
            List<int> GetTermData(ForwardOnlyDictionaryState state, string term)
            {
                return data[state.FieldName][term];
            }

            var enumerable = new ForwardOnlyDictionary<string, IDictionary<string, List<int>>>(null, data.Keys, GetFieldData);

            var sb = new StringBuilder();
            using (var writer = new StringWriter(sb))
                JsonSerializer.Create(new JsonSerializerSettings { Formatting = Formatting.None })
                    .Serialize(writer, enumerable);
            var result = sb.ToString();

            Assert.AreEqual("{\"aaa\":{\"aa\":[1,2,3],\"bb\":[4,5,6],\"cc\":[7,8,9],\"dd\":[1,3,5],\"ee\":[2,4,6]}," +
                            "\"bbb\":{\"aa\":[1,2,3],\"bb\":[4,5,6],\"cc\":[7,8,9],\"dd\":[1,3,5]}," +
                            "\"ccc\":{\"aa\":[1,2,3],\"bb\":[4,5,6],\"cc\":[7,8,9]}," +
                            "\"ddd\":{\"aa\":[1,2,3],\"bb\":[4,5,6]}," +
                            "\"eee\":{\"aa\":[1,2,3]}}", result);
        }
    }
}
