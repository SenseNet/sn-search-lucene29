﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Querying;
using SenseNet.Tools;

namespace SenseNet.Search.Lucene29
{
    /// <summary>
    /// Lucene29 search engine implementation. Loads the configured Lucene-specific query and indexing engines.
    /// </summary>
    public class Lucene29SearchEngine : ISearchEngine
    {
        private ILogger<Lucene29SearchEngine> _logger;

        /// <summary>
        /// Gets or sets the current indexing engine.
        /// </summary>
        public IIndexingEngine IndexingEngine { get; set; }

        /// <summary>
        /// Gets or sets the current query engine.
        /// </summary>
        public IQueryEngine QueryEngine { get; set; }

        public Lucene29SearchEngine(IIndexingEngine indexingEngine, IQueryEngine queryEngine, ILogger<Lucene29SearchEngine> logger)
        {
            // The constructor parameter has to be the general interface, but we need to
            // check that the provided type is compatible with this engine.
            if (indexingEngine != null && !(indexingEngine is ILuceneIndexingEngine))
                throw new InvalidOperationException(
                    $"The type {indexingEngine.GetType().FullName} is not compatible with the Lucene search engine.");

            if (indexingEngine != null)
                IndexingEngine = indexingEngine;
            if (queryEngine != null)
                QueryEngine = queryEngine;

            _logger = logger;
        }

        static Lucene29SearchEngine()
        {
            Lucene.Net.Search.BooleanQuery.SetMaxClauseCount(100000);
        }

        private IDictionary<string, IndexFieldAnalyzer> _analyzers = new Dictionary<string, IndexFieldAnalyzer>();

        /// <inheritdoc/>
        /// <summary>
        /// Returns the analyzers that were previously stored by the <see cref="SetIndexingInfo"/> method.
        /// </summary>
        public IDictionary<string, IndexFieldAnalyzer> GetAnalyzers()
        {
            return _analyzers;
        }

        /// <inheritdoc />
        /// <remarks>Passes indexinginfo to the underlying ILuceneIndexingEngine instance.</remarks>
        public void SetIndexingInfo(IDictionary<string, IPerFieldIndexingInfo> indexingInfo)
        {
            var analyzerTypes = new Dictionary<string, IndexFieldAnalyzer>();


            foreach (var item in indexingInfo)
            {
                var fieldName = item.Key;
                var fieldInfo = item.Value;
                if (fieldInfo.Analyzer != IndexFieldAnalyzer.Default)
                {
                    analyzerTypes.Add(fieldName, fieldInfo.Analyzer);
                }
            }

            var msg = $"Lucene29SearchEngine: indexing info table is received. " +
                      $"{indexingInfo.Count} fields, {analyzerTypes.Count} analyzer types. ";
            SnTrace.System.Write(msg);
            _logger?.LogInformation(msg);

            _analyzers = analyzerTypes;

            // Indexing info is stored in memory in the indexing engine
            // and should be refreshed when the list changes.
            ((ILuceneIndexingEngine)IndexingEngine).SetIndexingInfo(indexingInfo);
        }

        public object GetConfigurationForHealthDashboard()
        {
            return ((ILuceneIndexingEngine) IndexingEngine).GetConfigurationInfo();
        }

        public Task<object> GetHealthAsync(CancellationToken cancel)
        {
            throw new NotImplementedException();
        }
    }
}
