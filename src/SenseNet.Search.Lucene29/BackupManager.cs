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

        public Task BackupAsync(IndexingActivityStatus state, string backupDirectoryPath,
            LuceneSearchManager indexManager, CancellationToken cancellationToken)
        {
            Console.WriteLine("BACKUP START");
            Console.WriteLine("  IndexingActivityStatus: " + state);

            BackupInfo.StartedAt = DateTime.UtcNow;

            EnsureEmptyBackupDirectory(backupDirectoryPath);

            using (var snapshot = indexManager.CreateSnapshot(state))
                CopyIndexFiles(snapshot, indexManager, backupDirectoryPath);

            BackupInfo.FinishedAt = DateTime.UtcNow;

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

            CalculateInitialProgress(snapshot, backupDirectoryPath);

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
            var sourceFile = new FileInfo(Path.Combine(source, fileName));
            if (!File.Exists(targetPath))
            {
                OnCopyStart(fileName);
                File.Copy(sourceFile.FullName, targetPath);
//UNDONE:- Remove this line
Thread.Sleep(2000);
                Console.WriteLine("ok.");
                OnCopyFinish(sourceFile);
            }
            else
            {
                Console.WriteLine("skipped.");
            }
        }

        private void CalculateInitialProgress(IndexSnapshot snapshot, string backupDirectoryPath)
        {
            BackupInfo.CountOfFiles = snapshot.FileNames.Length;
            BackupInfo.TotalBytes = snapshot.FileNames
                .Sum(x => new FileInfo(Path.Combine(backupDirectoryPath, x)).Length);
        }
        private void OnCopyStart(string startingFile)
        {
            BackupInfo.CurrentlyCopiedFile = startingFile;
        }
        private void OnCopyFinish(FileInfo finishedFile)
        {
            BackupInfo.CopiedFiles++;
            BackupInfo.CopiedBytes += finishedFile.Length;
            BackupInfo.CurrentlyCopiedFile = null;
        }
    }
}
