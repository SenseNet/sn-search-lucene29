using System;
using System.Collections.Generic;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;

namespace SenseNet.Search.Lucene29
{
    /// <summary>
    /// sensenet specific Lucene analyzer, equivalent of Lucene's PerFieldAnalyzerWrapper.
    /// </summary>
    public class SnPerFieldAnalyzerWrapper : Analyzer
    {
        private IDictionary<string, Analyzer> _analyzerInfo;

        private readonly Analyzer _defaultAnalyzer = new KeywordAnalyzer();
        private readonly Dictionary<IndexFieldAnalyzer, Analyzer> _analyzers = new Dictionary<IndexFieldAnalyzer, Analyzer>
        {
            {IndexFieldAnalyzer.Keyword, new KeywordAnalyzer()},
            {IndexFieldAnalyzer.Standard, new StandardAnalyzer(LuceneSearchManager.LuceneVersion)},
            {IndexFieldAnalyzer.Whitespace, new WhitespaceAnalyzer()}
        };

        internal SnPerFieldAnalyzerWrapper(IDictionary<string, Analyzer> analyzerInfo)
        {
            _analyzerInfo = analyzerInfo;
            if (analyzerInfo == null)
            {
                SnTrace.Index.Write("WARNING: Creating SnPerFieldAnalyzerWrapper without AnalyzerInfo");
                SnTrace.Index.Write(Environment.StackTrace);
            }
        }

        private Analyzer GetAnalyzer(string fieldName)
        {
            // Hard-code the _Text field
            if (fieldName == "_Text")
                return _analyzers[IndexFieldAnalyzer.Standard];

            // For everything else, ask the ContentTypeManager
            Analyzer analyzer = null;
            _analyzerInfo?.TryGetValue(fieldName, out analyzer);

            // Return with analyzer by indexing info  or the default analyzer if indexing info was not found.
            return analyzer ?? _defaultAnalyzer;
        }
        
        //========================================================================================== Analyzer API implementation

        public override TokenStream TokenStream(string fieldName, System.IO.TextReader reader)
        {
            var analyzer = GetAnalyzer(fieldName);
            return analyzer.TokenStream(fieldName, reader);
        }

        public override TokenStream ReusableTokenStream(string fieldName, System.IO.TextReader reader)
        {
            if (overridesTokenStreamMethod)
            {
                // LUCENE-1678: force fallback to tokenStream() if we
                // have been subclassed and that subclass overrides
                // tokenStream but not reusableTokenStream
                return TokenStream(fieldName, reader);
            }

            var analyzer = GetAnalyzer(fieldName);
            return analyzer.ReusableTokenStream(fieldName, reader);
        }

        /// <summary>Returns the positionIncrementGap from the analyzer assigned to fieldName </summary>
        public override int GetPositionIncrementGap(string fieldName)
        {
            var analyzer = GetAnalyzer(fieldName);
            return analyzer.GetPositionIncrementGap(fieldName);
        }

        /// <summary>Returns the offsetGap from the analyzer assigned to fiel</summary>
        public override int GetOffsetGap(Lucene.Net.Documents.Fieldable field)
        {
            var analyzer = GetAnalyzer(field.Name());
            return analyzer.GetOffsetGap(field);
        }

        public override string ToString()
        {
            // {{Aroush-2.9}} will 'analyzerMap.ToString()' work in the same way as Java's java.util.HashMap.toString()? 
            return "SnPerFieldAnalyzerWrapper(" + _analyzers + ", default=" + _defaultAnalyzer + ")";
        }

        public void RefreshAnalyzers(IDictionary<string, Analyzer> analyzerInfo)
        {
            SnTrace.Index.Write($"REFRESH SnPerFieldAnalyzerWrapper.AnalyzerInfo with {analyzerInfo.Count} items.");
            _analyzerInfo = analyzerInfo;
        }
    }
}
