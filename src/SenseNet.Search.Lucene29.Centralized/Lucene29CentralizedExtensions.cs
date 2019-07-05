using SenseNet.ContentRepository;
using SenseNet.Tools;

namespace SenseNet.Search.Lucene29
{
    public static class Lucene29CentralizedExtensions
    {
        public static IRepositoryBuilder UseLucene29CentralizedSearchEngine(this IRepositoryBuilder repositoryBuilder)
        {
            var searchEngine = new Lucene29SearchEngine()
            {
                IndexingEngine = new Lucene29CentralizedIndexingEngine(),
                QueryEngine = new Lucene29CentralizedQueryEngine()
            };

            repositoryBuilder.UseSearchEngine(searchEngine);

            return repositoryBuilder;
        }
    }
}
