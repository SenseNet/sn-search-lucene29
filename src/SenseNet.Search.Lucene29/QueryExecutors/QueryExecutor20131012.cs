using System;
using System.Collections.Generic;
using Lucene.Net.Documents;
using Lucene.Net.Search;
using SenseNet.Diagnostics;

namespace SenseNet.Search.Lucene29.QueryExecutors
{
    internal class QueryExecutor20131012 : LuceneQueryExecutor
    {
        protected override SearchResult DoExecute(SearchParams p)
        {
            p.skip = LucQuery.Skip;

            var maxtop = p.numDocs - p.skip;
            if (maxtop < 1)
                return SearchResult.Empty;

            //TODO: check r and r1 for null below (see hints)
            SearchResult totalHits = null;
            SearchResult partitionHits;

            var howManyList = new List<int>(Configuration.Lucene29.DefaultTopAndGrowth);
            if (howManyList[howManyList.Count - 1] == 0)
                howManyList[howManyList.Count - 1] = int.MaxValue;

            if (p.top < int.MaxValue)
            {
                var howMany = p.top;
                if ((long)howMany > maxtop)
                    howMany = maxtop;
                while (howManyList.Count > 0)
                {
                    if (howMany < howManyList[0])
                        break;
                    howManyList.RemoveAt(0);
                }
                howManyList.Insert(0, howMany);
            }

            var originalTop = p.top;
            for (var i = 0; i < howManyList.Count; i++)
            {
                var defaultTop = howManyList[i];
                if (defaultTop == 0)
                    defaultTop = p.numDocs;

                p.howMany = defaultTop;
                p.useHowMany = i < howManyList.Count - 1;
                var maxSize = i == 0 ? p.numDocs : totalHits.totalCount;
                p.collectorSize = Math.Min(defaultTop, maxSize - p.skip) + p.skip;
                var currentSkip = p.skip;

                partitionHits = Search(p);

                if (i == 0)
                    totalHits = partitionHits;
                else
                    totalHits.Add(partitionHits);
                p.skip += totalHits.nextIndex;
                p.top = originalTop - totalHits.result.Count;

                SnTrace.Query.Write($"Collector size: {p.collectorSize}, skip: {currentSkip}" +
                                    $", Current hits: {partitionHits.result.Count}" +
                                    $", collected hits: {totalHits.result.Count}" +
                                    $", total hits: {totalHits.totalCount}");
                if ( //                                                  Do not get the next partition if...
                    totalHits.result.Count == 0 //                       there is no any hit
                    || partitionHits.result.Count == 0 //                or there is no any hit in the current partition
                    || totalHits.result.Count >= originalTop //          or the result reaches the limit
                    || totalHits.result.Count >= totalHits.totalCount // or the result contains the all items
                   )
                    break;
            }
            return totalHits;
        }
        protected override void GetResultPage(ScoreDoc[] hits, SearchParams p, SearchResult r)
        {
            var result = new List<LucObject>();
            if (hits.Length == 0)
            {
                r.result = result;
                return;
            }

            var upperBound = hits.Length;
            var index = 0;
            while (true)
            {
                Document doc = p.searcher.Doc(hits[index].Doc);
                result.Add(new LucObject(doc));
                if (result.Count == p.top)
                {
                    index++;
                    break;
                }
                if (++index >= upperBound)
                    break;
            }
            r.nextIndex = index;
            r.result = result;
        }
    }
}
