using SenseNet.ContentRepository;
using SenseNet.Search.Lucene29;
using SenseNet.Tools;

// ReSharper disable once CheckNamespace
namespace SenseNet.Extensions.DependencyInjection
{
    public static class Lucene29LocalExtensions
    {
        public static IRepositoryBuilder UseLucene29LocalSearchEngine(this IRepositoryBuilder repositoryBuilder, string indexDirectoryPath = null)
        {
            var indexDirectory = string.IsNullOrEmpty(indexDirectoryPath)
                ? null
                : new IndexDirectory(null, indexDirectoryPath);

            var searchEngine = new Lucene29SearchEngine()
            {
                IndexingEngine = new Lucene29LocalIndexingEngine(indexDirectory),
                QueryEngine = new Lucene29LocalQueryEngine()
            };

            repositoryBuilder.UseSearchEngine(searchEngine);

            return repositoryBuilder;
        }
    }
}
