using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Index;
using SenseNet.ContentRepository.Storage;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29;
using SenseNet.Search.Querying;

namespace CentralizedIndexBackupTester
{
    public class ValidityWorker : IWorker
    {
        private ILuceneIndexingEngine _engine;

        public ValidityWorker(ILuceneIndexingEngine engine)
        {
            _engine = engine;
        }

        public async Task WorkAsync(CancellationToken cancellationToken)
        {
            var lastId = "";
            while (true)
            {
                // Exit if needed.
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("Work finished");
                    return;
                }
                Console.WriteLine("Work");

                SnTrace.Write("#### wait");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

                // Create one content (e.g. SystemFolder)...
                var id = await CreateDocAsync(cancellationToken);
                SnTrace.Write("#### document created: " + id);

                // Delete the last created content.
                if (lastId != "")
                {
                    await DeleteDocAsync(lastId, cancellationToken);
                    SnTrace.Write("#### document deleted: " + lastId);
                }

                // ... and memorize its Id as "lastId".
                lastId = id;
            }
        }

        private async Task<string> CreateDocAsync(CancellationToken cancellationToken)
        {
            var id = GetId();
            var doc = new IndexDocument();

            doc.Add(new IndexField("TestDocId", id, IndexingMode.Default, IndexStoringMode.Yes, IndexTermVector.Default));
            doc.Add(new IndexField("DocType", "DocumentForValidityTest", IndexingMode.Default, IndexStoringMode.Yes, IndexTermVector.Default));
            doc.Add(new IndexField("Time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), IndexingMode.Default, IndexStoringMode.Yes, IndexTermVector.Default));

            var docs = new[] {doc};
            await _engine.WriteIndexAsync(null, null, docs, cancellationToken);

            return id;
        }

        private int _id;
        private string GetId()
        {
            return "#" + (++_id);
        }

        private async Task DeleteDocAsync(string lastId, CancellationToken cancellationToken)
        {
            var term = new SnTerm("TestDocId", lastId);
            var terms = new[] { term };
            await _engine.WriteIndexAsync(terms, null, null, cancellationToken);
        }
    }
}
