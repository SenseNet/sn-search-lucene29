using System;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.Search.Lucene29;

namespace WorkerForWriteLockDeletionTests
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var delay = int.Parse(args[0]);
                var name = args[1];
                var path = args[2];

                var indexDirectory = new IndexDirectory(name, path);
                var engine = new Lucene29LocalIndexingEngine(indexDirectory);
                engine.StartAsync(null, false, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
                Task.Delay(delay).ConfigureAwait(false).GetAwaiter().GetResult();
                engine.ShutDownAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
            }

            Console.WriteLine("Press <enter> to exit...");
            Console.ReadLine();
        }
    }
}
