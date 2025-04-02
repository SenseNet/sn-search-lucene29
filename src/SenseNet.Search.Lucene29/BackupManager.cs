using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.Diagnostics;
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
            var backupDirectoryIsConfigured = false;
            if (backupDirectoryPath == null)
            {
                backupDirectoryIsConfigured = true;
                backupDirectoryPath = Configuration.Lucene29.IndexBackupDirectory;
            }

            using (var op = SnTrace.Index.StartOperation("BackupManager: INDEX BACKUP. Target: " + backupDirectoryPath))
            {
                SnTrace.Index.Write("BackupManager: IndexingActivityStatus: " + state);

                BackupInfo.StartedAt = DateTime.UtcNow;
                BackupInfo.TargetPath = backupDirectoryPath;

                BackupInfo.Message = "Ensure empty backup directory";
                EnsureEmptyBackupDirectory(backupDirectoryPath, backupDirectoryIsConfigured, cancellationToken);

                using (var snapshot = indexManager.CreateSnapshot(state))
                    CopyIndexFiles(snapshot, indexManager, backupDirectoryPath, cancellationToken);

                BackupInfo.FinishedAt = DateTime.UtcNow;

                BackupInfo.Message = cancellationToken.IsCancellationRequested ? "Canceled" : "Finished";

                op.Successful = true;
            }
        }

        public bool CheckDirectory(string backupDirectoryPath)
        {
            if (backupDirectoryPath == null)
                return true;

            if (!Directory.Exists(backupDirectoryPath))
                return true;

            var subDirs = Directory.GetDirectories(backupDirectoryPath);
            var files = Directory.GetFiles(backupDirectoryPath);
            return subDirs.Length + files.Length == 0;
        }

        private void EnsureEmptyBackupDirectory(string backupDirectoryPath, bool backupDirectoryIsConfigured, CancellationToken cancellationToken)
        {
            using (var op = SnTrace.Index.StartOperation("BackupManager: Prepare backup directory"))
            {
                if (!Directory.Exists(backupDirectoryPath))
                {
                    Directory.CreateDirectory(backupDirectoryPath);
                    SnTrace.Index.Write("BackupManager: backup directory created.");
                    op.Successful = true;
                    return;
                }

                var subDirs = Directory.GetDirectories(backupDirectoryPath);
                var files = Directory.GetFiles(backupDirectoryPath);
                if(!backupDirectoryIsConfigured && (subDirs.Length + files.Length > 0))
                    throw new InvalidOperationException("BackupManager: backup directory is not empty: " + backupDirectoryPath);

                foreach (var path in files)
                {
                    Task.Run(() => { File.Delete(path); }, cancellationToken)
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    SnTrace.Index.Write("BackupManager: file deleted: " + Path.GetFileName(path));
                }

                op.Successful = true;
            }

        }

        private void CopyIndexFiles(IndexSnapshot snapshot, LuceneSearchManager indexManager,
            string backupDirectoryPath, CancellationToken cancellationToken)
        {
            using (var op = SnTrace.Index.StartOperation("BackupManager: Copy index files."))
            {
                var sourceDirectoryPath = indexManager.IndexDirectory.CurrentDirectory;

                // Calculate initial progress
                BackupInfo.CountOfFiles = snapshot.FileNames.Length;
                BackupInfo.TotalBytes = snapshot.FileNames
                    .Sum(x => new FileInfo(Path.Combine(sourceDirectoryPath, x)).Length);

                SnTrace.Index.Write("BackupManager: count of files: {0}, total bytes: {1}",
                    BackupInfo.CountOfFiles, BackupInfo.TotalBytes);

                if (cancellationToken.IsCancellationRequested)
                {
                    SnTrace.Index.Write("BackupManager: canceled.");
                    return;
                }
                CopyFile(sourceDirectoryPath, backupDirectoryPath, snapshot.SegmentFileName, cancellationToken);

                foreach (var fileName in snapshot.FileNames)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        SnTrace.Index.Write("BackupManager: canceled.");
                        return;
                    }
                    CopyFile(sourceDirectoryPath, backupDirectoryPath, fileName, cancellationToken);
                }

                op.Successful = true;
            }
        }

        private void CopyFile(string sourceDirectory, string targetDirectory, string fileName, CancellationToken cancellationToken)
        {
            using (var op = SnTrace.Index.StartOperation("BackupManager: Copy index file: " + fileName))
            {
                var targetPath = Path.Combine(targetDirectory, fileName);
                var sourceFile = new FileInfo(Path.Combine(sourceDirectory, fileName));
                if (!File.Exists(targetPath))
                {
                    BackupInfo.CurrentlyCopiedFile = fileName;

                    CopyFile(sourceFile.FullName, targetPath, cancellationToken);

                    BackupInfo.CopiedFiles++;
                    BackupInfo.CopiedBytes += sourceFile.Length;
                    BackupInfo.CurrentlyCopiedFile = null;
                }
                else
                {
                    SnTrace.Index.Write("BackupManager: copy {0} skipped", fileName);
                }

                op.Successful = true;
            }
        }

        private void CopyFile(string sourceFullPath, string targetFullPath, CancellationToken cancellationToken)
        {
            Task.Run(() => { File.Copy(sourceFullPath, targetFullPath); }, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}
