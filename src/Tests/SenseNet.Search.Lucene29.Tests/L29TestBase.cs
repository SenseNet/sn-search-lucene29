using System;
using System.Runtime.CompilerServices;
using System.Threading;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.InMemory;
using SenseNet.ContentRepository.Storage.DataModel;
using SenseNet.ContentRepository.Storage.Security;
using SenseNet.Extensions.DependencyInjection;
using SenseNet.Tests.Core;
using Task = System.Threading.Tasks.Task;

namespace SenseNet.Search.Lucene29.Tests
{
    public class L29TestBase : TestBase
    {
        protected async Task L29Test(Action<RepositoryBuilder> initialize, Func<Task> callback, bool saveInitialDocuments = true, [CallerMemberName] string memberName = "")
        {
            var indexFolderName = $"Test_{memberName}_{Guid.NewGuid()}";
            var indexingEngine = new Lucene29LocalIndexingEngine(new IndexDirectory(indexFolderName));
            var searchEngine = new Lucene29SearchEngine(indexingEngine, new Lucene29LocalQueryEngine());

            await base.Test(builder =>
                {
                    builder.UseInitialData(InitialData.Load(InMemoryTestData.Instance,
                        InMemoryTestIndexDocuments.IndexDocuments));
                    // important: use a real local search engine instead of in-memory search
                    builder.UseSearchEngine(searchEngine);

                    initialize?.Invoke(builder);
                },
                async () =>
                {
                    if (saveInitialDocuments)
                    {
                        using (new SystemAccount())
                        {
                            await SaveInitialIndexDocumentsAsync(CancellationToken.None);
                        }
                    }

                    await callback();
                });
        }
        protected Task L29Test(Func<Task> callback, bool saveInitialDocuments = true, [CallerMemberName] string memberName = "")
        {
            return L29Test(null, callback, saveInitialDocuments, memberName);
        }
    }
}
