using Microsoft.Extensions.Logging;
using SenseNet.Search.Lucene29;
using SenseNet.Tools;

// ReSharper disable once CheckNamespace
namespace SenseNet.Extensions.DependencyInjection
{
    public static class Lucene29LocalExtensions
    {
        public static IRepositoryBuilder UseLucene29LocalSearchEngine(this IRepositoryBuilder repositoryBuilder,
            ILogger<Lucene29SearchEngine> logger, string indexDirectoryPath = null)
        {
            var indexDirectory = string.IsNullOrEmpty(indexDirectoryPath)
                ? null
                : new IndexDirectory(null, indexDirectoryPath);

            var searchEngine = new Lucene29SearchEngine(
                new Lucene29LocalIndexingEngine(indexDirectory),
                new Lucene29LocalQueryEngine(),
                logger);

            repositoryBuilder.UseSearchEngine(searchEngine);

            return repositoryBuilder;
        }
    }
}
