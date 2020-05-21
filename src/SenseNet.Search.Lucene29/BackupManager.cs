using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Index;
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
            using (var op = SnTrace.Index.StartOperation("BackupManager: INDEX BACKUP. Target: " + backupDirectoryPath))
            {
                SnTrace.Index.Write("BackupManager: IndexingActivityStatus: " + state);

                BackupInfo.StartedAt = DateTime.UtcNow;

                EnsureEmptyBackupDirectory(backupDirectoryPath, cancellationToken);

                using (var snapshot = indexManager.CreateSnapshot(state))
                    CopyIndexFiles(snapshot, indexManager, backupDirectoryPath, cancellationToken);

                BackupInfo.FinishedAt = DateTime.UtcNow;

                op.Successful = true;
            }
        }

        private void EnsureEmptyBackupDirectory(string backupDirectoryPath, CancellationToken cancellationToken)
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

                foreach (var path in Directory.GetFiles(backupDirectoryPath))
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

                CopyFile(sourceDirectoryPath, backupDirectoryPath, snapshot.SegmentFileName, cancellationToken);

                foreach (var fileName in snapshot.FileNames)
                    CopyFile(sourceDirectoryPath, backupDirectoryPath, fileName, cancellationToken);

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
