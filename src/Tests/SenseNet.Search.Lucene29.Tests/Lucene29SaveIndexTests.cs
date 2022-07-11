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
        public void _SaveIndex_ForwardOnlyDictionary()
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

            IDictionary<string, List<int>> GetFieldData(string field, string state)
            {
                var keys = data[field].Keys;
                return new ForwardOnlyDictionary<string, List<int>>(keys, field, GetTermData);
            }
            List<int> GetTermData(string term, string field)
            {
                return data[field][term];
            }

            var enumerable = new ForwardOnlyDictionary<string, IDictionary<string, List<int>>>(data.Keys, null, GetFieldData);

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
        public void L29_SaveIndex_ToFile(IDictionary<string, IDictionary<string, List<int>>> invertedIndex)
        {
                var transformedIndex = new Dictionary<string, Dictionary<string, string>>();
                var indexDocs = new Dictionary<int, Dictionary<string, string>>();
                foreach (var field in invertedIndex)
                {
                    var transformedItem = new Dictionary<string, string>();
                    transformedIndex.Add(field.Key, transformedItem);
                    if (field.Value == null)
                        continue;
                    foreach (var term in field.Value)
                    {
                        transformedItem.Add(term.Key, string.Join(",", term.Value.Distinct().Select(x => x.ToString()).ToArray()));
                        foreach (var doc in term.Value)
                        {
                            if (!indexDocs.TryGetValue(doc, out var document))
                                indexDocs.Add(doc, document = new Dictionary<string, string>());
                            if (!document.TryGetValue(field.Key, out var value))
                                document.Add(field.Key, term.Key);
                            else
                                document[field.Key] = $"{value}, {term.Key}";
                        }
                    }
                }

                using (var writer = new StreamWriter(@"D:\_InitialData\9\rawindex2.txt", false, Encoding.UTF8))
                    JsonSerializer.Create(new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore })
                        .Serialize(writer, transformedIndex);
                using (var writer = new StreamWriter(@"D:\_InitialData\9\indexdocs2.txt", false, Encoding.UTF8))
                    JsonSerializer.Create(new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore })
                        .Serialize(writer, indexDocs);
        }

        private class ForwardOnlyDictionary<TKey, TValue> : IDictionary<TKey, TValue>
        {
            private readonly List<TKey> _keys;
            private readonly string _state;
            private readonly Func<TKey, string, TValue> _itemGetter;

            public ForwardOnlyDictionary(IEnumerable<TKey> keys, string state, Func<TKey, string, TValue> itemGetter)
            {
                _keys = keys.OrderBy(x => x).ToList();
                _state = state;
                _itemGetter = itemGetter;
            }

            public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
            {
                foreach (var key in _keys)
                    yield return new KeyValuePair<TKey, TValue>(key, _itemGetter(key, _state));
            }

            IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

            public int Count => _keys.Count;
            public bool IsReadOnly => true;
            public ICollection<TKey> Keys => _keys;
            public bool ContainsKey(TKey key)
            {
                return _keys.Contains(key);
            }

            #region Not supported elements
            public void Add(KeyValuePair<TKey, TValue> item) { throw new NotSupportedException(); }
            public void Clear() { throw new NotSupportedException(); }
            public bool Contains(KeyValuePair<TKey, TValue> item) { throw new NotSupportedException(); }
            public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) { throw new NotSupportedException(); }
            public bool Remove(KeyValuePair<TKey, TValue> item) { throw new NotSupportedException(); }
            public void Add(TKey key, TValue value) { throw new NotSupportedException(); }
            public bool Remove(TKey key) { throw new NotSupportedException(); }
            public bool TryGetValue(TKey key, out TValue value) { throw new NotSupportedException(); }
            public TValue this[TKey key]
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }
            public ICollection<TValue> Values => throw new NotSupportedException();
            #endregion;
        }
    }
}
