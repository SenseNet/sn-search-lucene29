using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Index;
using SenseNet.Search.Indexing;

namespace SenseNet.Search.Lucene29
{
    internal class IndexSnapshot : IDisposable
    {
        private readonly SnapshotDeletionPolicy _snapshotMaker;

        public string[] FileNames { get; }
        public string SegmentFileName { get; }

        public IndexSnapshot(SnapshotDeletionPolicy snapshotMaker)
        {
            _snapshotMaker = snapshotMaker;

            var indexCommitPoint = _snapshotMaker.Snapshot();
            FileNames = indexCommitPoint.GetFileNames().ToArray();
            SegmentFileName = indexCommitPoint.GetSegmentsFileName();
        }
        public void Dispose()
        {
            _snapshotMaker.Release();
        }
    }

    public class BackupManager : IBackupManager, IBackupManagerFactory
    {
        public IBackupManager CreateBackupManager()
        {
            return new BackupManager();
        }

        public Task BackupAsync(IndexingActivityStatus state, string backupDirectoryPath,
            LuceneSearchManager indexManager, CancellationToken cancellationToken)
        {
            //UNDONE:! write SnTrace lines.
            Console.WriteLine("BACKUP START");
            Console.WriteLine("  IndexingActivityStatus: " + state);
            EnsureEmptyBackupDirectory(backupDirectoryPath);

            using (var snapshot = indexManager.CreateSnapshot(state))
                CopyIndexFiles(snapshot, indexManager, backupDirectoryPath);

            return Task.CompletedTask;
        }

        private void EnsureEmptyBackupDirectory(string backupDirectoryPath)
        {
            Console.Write("Prepare backup directory: ");

            if (!Directory.Exists(backupDirectoryPath))
            {
                Directory.CreateDirectory(backupDirectoryPath);
                Console.WriteLine("created");
                return;
            }

            var deleted = 0;
            foreach (var path in Directory.GetFiles(backupDirectoryPath))
            {
                File.Delete(path);
                deleted++;
            }
            Console.WriteLine($"{deleted} files deleted.");
        }

        private void CopyIndexFiles(IndexSnapshot snapshot, LuceneSearchManager indexManager, string backupDirectoryPath)
        {
            var timer = Stopwatch.StartNew();

            var source = indexManager.IndexDirectory.CurrentDirectory;

            Console.WriteLine("CopyIndexFiles starts.");
            Console.WriteLine("  Source: " + source);
            Console.WriteLine("  Target: " + backupDirectoryPath);

            Console.WriteLine("  SegmentFile: ");
            CopyFile(source, backupDirectoryPath, snapshot.SegmentFileName);

            Console.WriteLine("  Files: ");
            foreach (var fileName in snapshot.FileNames)
                CopyFile(source, backupDirectoryPath, fileName);

            timer.Stop();
            Console.WriteLine("CopyIndexFiles finished. Elapsed time: " + timer.Elapsed);
        }
        private void CopyFile(string source, string target, string fileName)
        {
            Console.Write("    " + fileName + ": ");

            var targetPath = Path.Combine(target, fileName);
            if (!File.Exists(targetPath))
            {
                var sourcePath = Path.Combine(source, fileName);
                File.Copy(sourcePath, targetPath);
                Console.WriteLine("ok.");
            }
            else
            {
                Console.WriteLine("skipped.");
            }
        }
    }
}
