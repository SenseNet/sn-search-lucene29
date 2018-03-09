using System;
using System.Collections.Generic;
using System.Linq;
using SenseNet.Search.Indexing;

namespace SenseNet.Search.Lucene29.Centralized.Service
{
    public class SearchManager
    {
        public static LuceneSearchManager Instance { get; } = new LuceneSearchManager(new IndexDirectory());
        //private static IDictionary<string, IPerFieldIndexingInfo> _perFieldIndexingInfo;

        //public static IPerFieldIndexingInfo GetPerFieldIndexingInfo(string fieldName)
        //{
        //    //UNDONE: how do we get Aspect field indexing infos?
        //    if (fieldName.Contains('.'))
        //        throw new NotImplementedException("Aspect field indexing infos are not accessible.");

        //    return _perFieldIndexingInfo.TryGetValue(fieldName, out var info) ? info : null;
        //}

        //public static void SetPerFieldIndexingInfo(IDictionary<string, IPerFieldIndexingInfo> perFieldIndexingInfo)
        //{
        //    _perFieldIndexingInfo = perFieldIndexingInfo;
        //}
    }
}
