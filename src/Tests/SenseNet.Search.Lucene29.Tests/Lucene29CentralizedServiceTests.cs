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
        public void L29_Service_OnlyOneBackup()
        {
            var service = new SearchService { BackupManagerFactory = new BackupManager_for_OnlyOneBackupTest() };

            var tasks = Enumerable.Range(0, 5)
                .Select(x => Task.Run(() => service.Backup(null, null)))
                .ToArray();
            // ReSharper disable once CoVariantArrayConversion
            Task.WaitAll(tasks);

            var completed = new string(tasks.Select(t => (char)((int)'0' + (int)t.Result.State)).ToArray());
            var faulted = new string(tasks.Select(t => t.IsFaulted ? 'Y' : 'n').ToArray());

            // 1 = Started, 2 = AlreadyStarted,
            Assert.AreEqual("12222", completed);
            Assert.AreEqual("nnnnn", faulted);
        }
        #region private class BackupManager_for_OnlyOneBackupTest
        // ReSharper disable once InconsistentNaming
        private class BackupManager_for_OnlyOneBackupTest : IBackupManager, IBackupManagerFactory
        {
            public IBackupManager CreateBackupManager()
            {
                return new BackupManager_for_OnlyOneBackupTest();
            }
            public BackupInfo BackupInfo { get; } = new BackupInfo();
            public async Task BackupAsync(IndexingActivityStatus state, string backupDirectoryPath,
                LuceneSearchManager indexManager, CancellationToken cancellationToken)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        #endregion

        [TestMethod]
        public void L29_Service_Progress()
        {
            BackupResponse response;
            var responses = new List<BackupResponse>();
            var service = new SearchService {BackupManagerFactory = new BackupManager_for_ProgressTest()};

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
            public async Task BackupAsync(IndexingActivityStatus state, string backupDirectoryPath,
                LuceneSearchManager indexManager, CancellationToken cancellationToken)
            {
                BackupInfo.StartedAt = DateTime.UtcNow;

                BackupInfo.TotalBytes = 3 * 42L;
                BackupInfo.CountOfFiles = 3;

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                BackupInfo.CurrentlyCopiedFile = "File1";

                for (int i = 0; i < 3; i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

                    BackupInfo.CopiedBytes += 42;
                    BackupInfo.CopiedFiles++;
                    BackupInfo.CurrentlyCopiedFile = i < 2 ? $"File{i + 2}" : null;
                }

                BackupInfo.FinishedAt = DateTime.UtcNow;
            }
        }
        #endregion

    }
}
