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
    public class BackupManager : IBackupManager, IBackupManagerFactory
    {
        public BackupInfo BackupInfo { get; } = new BackupInfo();

        public IBackupManager CreateBackupManager()
        {
            return new BackupManager();
        }

        public void Backup(IndexingActivityStatus state, string backupDirectoryPath,
            LuceneSearchManager indexManager, CancellationToken cancellationToken)
        {
            Console.WriteLine("BACKUP START");
            Console.WriteLine("  IndexingActivityStatus: " + state);

            BackupInfo.StartedAt = DateTime.UtcNow;

            EnsureEmptyBackupDirectory(backupDirectoryPath, cancellationToken);

            using (var snapshot = indexManager.CreateSnapshot(state))
                CopyIndexFiles(snapshot, indexManager, backupDirectoryPath, cancellationToken);

            BackupInfo.FinishedAt = DateTime.UtcNow;
        }

        private void EnsureEmptyBackupDirectory(string backupDirectoryPath, CancellationToken cancellationToken)
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
                Task.Run(() => { File.Delete(path); }, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                deleted++;
            }

            Console.WriteLine($"{deleted} files deleted.");
        }

        private void CopyIndexFiles(IndexSnapshot snapshot, LuceneSearchManager indexManager,
            string backupDirectoryPath, CancellationToken cancellationToken)
        {
            var timer = Stopwatch.StartNew();

            var sourceDirectoryPath = indexManager.IndexDirectory.CurrentDirectory;

            // Calculate initial progress
            BackupInfo.CountOfFiles = snapshot.FileNames.Length;
            BackupInfo.TotalBytes = snapshot.FileNames
                .Sum(x => new FileInfo(Path.Combine(sourceDirectoryPath, x)).Length);

            Console.WriteLine("CopyIndexFiles starts.");
            Console.WriteLine("  Source: " + sourceDirectoryPath);
            Console.WriteLine("  Target: " + backupDirectoryPath);

            Console.WriteLine("  SegmentFile: ");
            CopyFile(sourceDirectoryPath, backupDirectoryPath, snapshot.SegmentFileName, cancellationToken);

            Console.WriteLine("  Files: ");
            foreach (var fileName in snapshot.FileNames)
                CopyFile(sourceDirectoryPath, backupDirectoryPath, fileName, cancellationToken);

            timer.Stop();
            Console.WriteLine("CopyIndexFiles finished. Elapsed time: " + timer.Elapsed);
        }

        private void CopyFile(string sourceDirectory, string targetDirectory, string fileName, CancellationToken cancellationToken)
        {
            Console.Write("    " + fileName + ": ");

            var targetPath = Path.Combine(targetDirectory, fileName);
            var sourceFile = new FileInfo(Path.Combine(sourceDirectory, fileName));
            if (!File.Exists(targetPath))
            {
                BackupInfo.CurrentlyCopiedFile = fileName;

                CopyFile(sourceFile.FullName, targetPath, cancellationToken);

                Console.WriteLine("ok.");

                BackupInfo.CopiedFiles++;
                BackupInfo.CopiedBytes += sourceFile.Length;
                BackupInfo.CurrentlyCopiedFile = null;
            }
            else
            {
                Console.WriteLine("skipped.");
            }
        }

        private void CopyFile(string sourceFullPath, string targetFullPath, CancellationToken cancellationToken)
        {
            Task.Run(() => { File.Copy(sourceFullPath, targetFullPath); }, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}
