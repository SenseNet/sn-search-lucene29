using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Index;
using SenseNet.Tests;

namespace SenseNet.Search.Lucene29.Tests
{
    [TestClass]
    public class Lucene29CentralizedServiceTests : TestBase
    {
        [TestMethod]
        public void L29_Service_Backup_OnlyOne()
        {
            SearchService.BackupManagerFactory = new BackupManager_for_OnlyOneTest();
            var service = new SearchService();

            var tasks = Enumerable.Range(0, 5)
                .Select(x => Task.Run(() => service.Backup(null, null)))
                .ToArray();
            // ReSharper disable once CoVariantArrayConversion
            Task.WaitAll(tasks);

            var completed = new string(tasks
                .Select(t => (char) ((int) '0' + (int) t.Result.State))
                .OrderBy(x => x)
                .ToArray());
            var faulted = new string(tasks
                .Select(t => t.IsFaulted ? 'Y' : 'n')
                .ToArray());

            // 1 = Started, 2 = AlreadyStarted,
            Assert.AreEqual("12222", completed);
            Assert.AreEqual("nnnnn", faulted);
        }
        #region private class BackupManager_for_OnlyOneTest
        // ReSharper disable once InconsistentNaming
        private class BackupManager_for_OnlyOneTest : IBackupManager, IBackupManagerFactory
        {
            public IBackupManager CreateBackupManager()
            {
                return new BackupManager_for_OnlyOneTest();
            }
            public BackupInfo BackupInfo { get; } = new BackupInfo();
            public void Backup(IndexingActivityStatus state, string backupDirectoryPath,
                LuceneSearchManager indexManager, CancellationToken cancellationToken)
            {
                Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).GetAwaiter().GetResult();
            }
        }
        #endregion

        [TestMethod]
        public void L29_Service_Backup_Progress()
        {
            SearchService.BackupManagerFactory = new BackupManager_for_ProgressTest();
            var service = new SearchService();

            BackupResponse response;
            var responses = new List<BackupResponse>();

            responses.Add(response = service.Backup(null, null));
            var backupInfo = response.Current;
            Assert.IsNotNull(backupInfo);
            Assert.AreEqual(BackupState.Started, response.State);

            var timeout = TimeSpan.FromSeconds(5.0d);
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < timeout)
            {
                Thread.Sleep(400);
                responses.Add(response = service.QueryBackup());
                if (response.State != BackupState.AlreadyStarted)
                    break;
            }

            var states = responses.Select(r => r.State).Distinct().ToArray();
            var bytes = responses.Select(r => (r.Current ?? r.History[0]).CopiedBytes).Distinct().ToArray();
            var files = responses.Select(r => (r.Current ?? r.History[0]).CopiedFiles).Distinct().ToArray();
            var names = responses.Select(r => (r.Current ?? r.History[0]).CurrentlyCopiedFile ?? "").Distinct().ToArray();

            AssertSequenceEqual(new[] { BackupState.Started, BackupState.AlreadyStarted, BackupState.Finished }, states);
            AssertSequenceEqual(new long[] { 0, 42, 2 * 42, 3 * 42 }, bytes);
            AssertSequenceEqual(new [] { 0, 1, 2, 3 }, files);
            AssertSequenceEqual(new [] { "", "File1", "File2", "File3" }, names);
        }
        #region private class BackupManager_for_ProgressTest
        // ReSharper disable once InconsistentNaming
        private class BackupManager_for_ProgressTest : IBackupManager, IBackupManagerFactory
        {
            public IBackupManager CreateBackupManager()
            {
                return new BackupManager_for_ProgressTest();
            }
            public BackupInfo BackupInfo { get; } = new BackupInfo();
            /// <summary>
            /// RUNS NOT EXCLUSIVE. DO NOT CALL TWICE.
            /// </summary>
            public void Backup(IndexingActivityStatus state, string backupDirectoryPath,
                LuceneSearchManager indexManager, CancellationToken cancellationToken)
            {
                BackupInfo.StartedAt = DateTime.UtcNow;

                BackupInfo.TotalBytes = 3 * 42L;
                BackupInfo.CountOfFiles = 3;

                Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
                BackupInfo.CurrentlyCopiedFile = "File1";

                for (int i = 0; i < 3; i++)
                {
                    Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

                    BackupInfo.CopiedBytes += 42;
                    BackupInfo.CopiedFiles++;
                    BackupInfo.CurrentlyCopiedFile = i < 2 ? $"File{i + 2}" : null;
                }

                BackupInfo.FinishedAt = DateTime.UtcNow;
            }
        }
        #endregion

        [TestMethod]
        public void L29_Service_Backup_Cancellation()
        {
            SearchService.BackupManagerFactory = new BackupManager_for_CancellationTest();
            var service = new SearchService();

            BackupResponse response;
            var responses = new List<BackupResponse>();

            responses.Add(response = service.Backup(null, null));
            var backupInfo = response.Current;
            Assert.IsNotNull(backupInfo);
            Assert.AreEqual(BackupState.Started, response.State);

            var timeout = TimeSpan.FromSeconds(5.0d);
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < timeout)
            {
                Thread.Sleep(400);
                responses.Add(response = service.QueryBackup());
                if (response.State != BackupState.AlreadyStarted)
                    break;
            }
            Thread.Sleep(400);
            responses.Add(service.CancelBackup());
            Thread.Sleep(400);
            responses.Add(service.QueryBackup());

            var states = responses.Select(r => r.State).Distinct().ToArray();
            var bytes = responses.Select(r => (r.Current ?? r.History[0]).CopiedBytes).Distinct().ToArray();
            var files = responses.Select(r => (r.Current ?? r.History[0]).CopiedFiles).Distinct().ToArray();
            var names = responses.Select(r => (r.Current ?? r.History[0]).CurrentlyCopiedFile ?? "").Distinct().ToArray();
            var messages = responses.Select(r => (r.Current ?? r.History[0]).Message ?? "").Distinct().ToArray();

            var count = files.Length;
            var expectedStates = new[]
            {
                BackupState.Started, BackupState.AlreadyStarted,
                BackupState.CancelRequested, BackupState.Canceled
            };
            var expectedFiles = Enumerable.Range(0, count).ToArray();
            var expectedBytes = expectedFiles.Select(x => x * 42L).ToArray();
            var expectedNames = (new[] {""}).Union(expectedFiles.Select(x => $"File{x + 1}")).ToArray();

            AssertSequenceEqual(expectedStates, states);
            AssertSequenceEqual(expectedBytes, bytes);
            AssertSequenceEqual(expectedFiles, files);
            AssertSequenceEqual(expectedNames, names);
        }
        #region private class BackupManager_for_CancellationTest
        // ReSharper disable once InconsistentNaming
        private class BackupManager_for_CancellationTest : IBackupManager, IBackupManagerFactory
        {
            public IBackupManager CreateBackupManager()
            {
                return new BackupManager_for_CancellationTest();
            }
            public BackupInfo BackupInfo { get; } = new BackupInfo();

            /// <summary>
            /// RUNS NOT EXCLUSIVE. DO NOT CALL TWICE.
            /// </summary>
            public void Backup(IndexingActivityStatus state, string backupDirectoryPath,
                LuceneSearchManager indexManager, CancellationToken cancellationToken)
            {
                BackupInfo.StartedAt = DateTime.UtcNow;

                BackupInfo.TotalBytes = 333333333L;
                BackupInfo.CountOfFiles = 33333;

                Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
                BackupInfo.CurrentlyCopiedFile = "File1";

                var i = 0;
                while (true)
                {
                    Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

                    BackupInfo.CopiedBytes += 42;
                    BackupInfo.CopiedFiles++;
                    BackupInfo.CurrentlyCopiedFile = $"File{++i + 1}";
                }
                // ReSharper disable once FunctionNeverReturns
            }
        }
        #endregion

    }
}
