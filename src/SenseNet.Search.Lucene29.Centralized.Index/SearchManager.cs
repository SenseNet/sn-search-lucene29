﻿using System.Collections.Generic;

namespace SenseNet.Search.Lucene29.Centralized.Index
{
    public class SearchManager
    {
        public static LuceneSearchManager Instance { get; } = new LuceneSearchManager(
            new IndexDirectory(indexDirectoryPath: SenseNet.Configuration.Indexing.IndexDirectoryFullPath));

        public static IDictionary<string, string> SortFieldNames { get; set; }
    }
}
