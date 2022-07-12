using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Lucene.Net.Index;

namespace SenseNet.Search.Lucene29
{
    /// <summary>
    /// Represents a state variables in the ForwardOnlyDictionary enumerations.
    /// </summary>
    public class ForwardOnlyDictionaryState
    {
        /// <summary>
        /// Gets the opened <see cref="IndexReader"/>.
        /// </summary>
        public IndexReader IndexReader { get; }
        /// <summary>
        /// Gets or sets the current field name or null.
        /// </summary>
        public string FieldName { get; set; }
        public CancellationToken Cancellation { get; }

        /// <summary>
        /// Initializes the instance of the <see cref="ForwardOnlyDictionaryState"/>
        /// </summary>
        /// <param name="ixReader">An opened <see cref="IndexReader"/>.</param>
        /// <param name="cancel"></param>
        public ForwardOnlyDictionaryState(IndexReader ixReader, CancellationToken cancel)
        {
            IndexReader = ixReader;
            Cancellation = cancel;
        }
    }
    /// <summary>
    /// Represents generic key/value pairs for sequential reading only.
    /// Modification and random access is not supported.
    /// </summary>
    /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    public class ForwardOnlyDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        private readonly List<TKey> _keys;
        private readonly ForwardOnlyDictionaryState _state;
        private readonly Func<ForwardOnlyDictionaryState, TKey, TValue> _itemGetter;
        private readonly Func<ForwardOnlyDictionaryState, TKey, TKey> _keyTransformer;

        /// <summary>
        /// Initializes a new instance of the ForwardOnlyDictionary&lt;TKey, TValue&gt;.
        /// </summary>
        /// <param name="keys">Keys that will be ordered and enumerated for itemGetter function.</param>
        /// <param name="state">Contains data for itemGetter function.</param>
        /// <param name="itemGetter">Function that gets a value by passed key and state.</param>
        /// <param name="keyTransformer">Function that converts the key if needed.</param>
        public ForwardOnlyDictionary(ForwardOnlyDictionaryState state, IEnumerable<TKey> keys,
            Func<ForwardOnlyDictionaryState, TKey, TValue> itemGetter,
            Func<ForwardOnlyDictionaryState, TKey, TKey> keyTransformer = null)
        {
            _keys = keys.OrderBy(x => x).ToList();
            _state = state;
            _itemGetter = itemGetter;
            _keyTransformer = keyTransformer;
        }

        /// <inheritdoc/>
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            foreach (var key in _keys)
            {
                var transformedKey = _keyTransformer == null ? key : _keyTransformer.Invoke(_state, key);
                if (transformedKey != null)
                    yield return new KeyValuePair<TKey, TValue>(transformedKey, _itemGetter(_state, key));
            }
        }
        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        /// <inheritdoc/>
        public int Count => _keys.Count;
        /// <summary>Returns true.</summary>
        public bool IsReadOnly => true;
        /// <inheritdoc/>
        public ICollection<TKey> Keys => _keys;
        /// <inheritdoc/>
        public bool ContainsKey(TKey key)
        {
            return _keys.Contains(key);
        }

        #region Not supported elements
        /// <summary>Not supported.</summary>
        public void Add(KeyValuePair<TKey, TValue> item) { throw new NotSupportedException(); }
        /// <summary>Not supported.</summary>
        public void Clear() { throw new NotSupportedException(); }
        /// <summary>Not supported.</summary>
        public bool Contains(KeyValuePair<TKey, TValue> item) { throw new NotSupportedException(); }
        /// <summary>Not supported.</summary>
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) { throw new NotSupportedException(); }
        /// <summary>Not supported.</summary>
        public bool Remove(KeyValuePair<TKey, TValue> item) { throw new NotSupportedException(); }
        /// <summary>Not supported.</summary>
        public void Add(TKey key, TValue value) { throw new NotSupportedException(); }
        /// <summary>Not supported.</summary>
        public bool Remove(TKey key) { throw new NotSupportedException(); }
        /// <summary>Not supported.</summary>
        public bool TryGetValue(TKey key, out TValue value) { throw new NotSupportedException(); }
        /// <summary>Not supported.</summary>
        public TValue this[TKey key]
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        /// <summary>Not supported.</summary>
        public ICollection<TValue> Values => throw new NotSupportedException();
        #endregion;
    }
}
