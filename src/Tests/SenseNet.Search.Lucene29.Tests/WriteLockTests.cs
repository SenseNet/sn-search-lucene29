using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.Diagnostics;

namespace SenseNet.Search.Lucene29.Tests
{
    [TestClass]
    public class WriteLockTests
    {
        private class TestSnTracer : ISnTracer
        {
            public List<string> Lines { get; } = new List<string>();
            public void Write(string line)
            {
                Lines.Add(line);
            }

            public void Flush() { }
        }

        [TestMethod]
        public void WriteLock_DeletionAfterKilledProcess()
        {
            var tracer = GetTracer();
            SnTrace.EnableAll();

            // Ensure an existing but free index directory
            SnTrace.Write("1 creating index");
            var indexDir = EnsureIndexDirectoryAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            SnTrace.Write("2 index created");

            // Start a process that use the directory
            var exePath = Path.GetFullPath(Path.Combine(
                System.Reflection.Assembly.GetExecutingAssembly().Location,
                "..\\..\\..\\..\\..\\WorkerForWriteLockDeletionTests\\bin\\Debug\\net8.0\\WorkerForWriteLockDeletionTests.exe"));

            var process = Process.Start(exePath, $"100000 {indexDir.Name} {indexDir.Path}");
            if (process == null)
                Assert.Fail("Cannot start the process.");

            SnTrace.Write("3 process started");

            // Wait for the indexing engine uses the index
            while (!File.Exists(indexDir.LockPath))
                Task.Delay(100).ConfigureAwait(false).GetAwaiter().GetResult();
            SnTrace.Write("4 index locked");

            // Start the new indexing engine in async way.
            SnTrace.Write("5 starting new engine");
            Task.Run(() => { StartNewEngineAsync(indexDir.Name, indexDir.Path).ConfigureAwait(false); });

            SnTrace.Write("6 wait a bit");
            // Wait a bit
            Task.Delay(1000).ConfigureAwait(false).GetAwaiter().GetResult();

            // Check the console: the new engine cannot delete the write.lock file
            var lastLine1 = tracer.Lines[tracer.Lines.Count-1];

            // Kill the lock owner process
            SnTrace.Write("7 killing the process");
            process.Kill();
//Task.Delay(20).ConfigureAwait(false).GetAwaiter().GetResult();
//File.Delete(indexDir.LockPath);
            SnTrace.Write("8 wait a bit");
            // lock file remains but deletable
            Task.Delay(2000).ConfigureAwait(false).GetAwaiter().GetResult();

            SnTrace.Write("9 test finished");

            // Check the console: the new engine has started
            Assert.IsTrue(tracer.Lines.Any(x => x.EndsWith("101 started")));
        }

        private TestSnTracer GetTracer()
        {
            var old = SnTrace.SnTracers.FirstOrDefault(x => x.GetType() == typeof(TestSnTracer));
            if (old != null)
                SnTrace.SnTracers.Remove(old);

            var tracer = new TestSnTracer();
            SnTrace.SnTracers.Add(tracer);
            return tracer;
        }

        private async Task<(string Name, string Path, string LockPath)> EnsureIndexDirectoryAsync()
        {
            var indexDirectory = new IndexDirectory("WriteLockTests", null);
            var engine = new Lucene29LocalIndexingEngine(indexDirectory);
            await engine.StartAsync(null, false, CancellationToken.None).ConfigureAwait(false);

            while (!File.Exists(indexDirectory.IndexLockFilePath))
                Task.Delay(50).ConfigureAwait(false).GetAwaiter().GetResult();

            await engine.ShutDownAsync(CancellationToken.None).ConfigureAwait(false);

            while (File.Exists(indexDirectory.IndexLockFilePath))
                Task.Delay(50).ConfigureAwait(false).GetAwaiter().GetResult();

            var name = indexDirectory.Name;
            var path = Path.GetDirectoryName(indexDirectory.CurrentDirectory);

            return (name, path, indexDirectory.IndexLockFilePath);
        }

        private async Task StartNewEngineAsync(string name, string path)
        {
            var sb = new StringBuilder();
            var console = new StringWriter(sb);
            var indexDirectory = new IndexDirectory(name, path);
            var engine = new Lucene29LocalIndexingEngine(indexDirectory);

            SnTrace.Write("100 starting");
            await engine.StartAsync(console, false, CancellationToken.None).ConfigureAwait(false);
            SnTrace.Write("101 started");

            while (!File.Exists(indexDirectory.IndexLockFilePath))
                Task.Delay(50).ConfigureAwait(false).GetAwaiter().GetResult();
            await engine.ShutDownAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
