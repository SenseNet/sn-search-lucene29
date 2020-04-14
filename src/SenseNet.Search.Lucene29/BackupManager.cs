using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Lucene.Net.Index;

namespace SenseNet.Search.Lucene29
{
    public class BackupManager
    {
        private readonly string _backupDirectoryPath;
        private readonly ILuceneIndexingEngine _engine;
        private readonly LuceneSearchManager _indexManager;

        public BackupManager(string backupDirectoryPath, ILuceneIndexingEngine engine)
        {
            _backupDirectoryPath = backupDirectoryPath;
            _engine = engine;
            _indexManager = engine.LuceneSearchManager;
        }

        public Task BackupAsync()
        {
            EnsureEmptyBackupDirectory(_backupDirectoryPath);
            CreateMultipleIndex();
            CopyIndexFiles();
            NotifyCallerThatTheBackupIsComplete();
            MergeIndexes();
            NotifySystemThatTheBackupIsComplete();

            return Task.CompletedTask;
        }

        /* ======================================================================= ENSURE BACKUP DIRECTORY */
        private void EnsureEmptyBackupDirectory(string backupDirectoryPath)
        {
            if (!Directory.Exists(backupDirectoryPath))
            {
                Directory.CreateDirectory(backupDirectoryPath);
                return;
            }

            foreach(var path in Directory.GetFiles(backupDirectoryPath))
                File.Delete(path);
        }

        /* ======================================================================= CREATE MULTIPLE INDEX */
        private void CreateMultipleIndex()
        {
            Console.WriteLine("CreateMultipleIndex starts.");
            var timer = Stopwatch.StartNew();

            var snapshotPath = _indexManager.IndexDirectory.CurrentDirectory;
            var currentPath = _indexManager.IndexDirectory.CreateNew();
            _indexManager.ShutDown();

            PauseIndexWriting();

            var snapshotReader = OpenReader(snapshotPath);

            var currentReader = OpenWriterAndReader(currentPath);

            var multipleReader = OpenMultipleReader(snapshotReader, currentReader);

            ContinueIndexWriting();
            Task.Delay(TimeSpan.FromSeconds(1)).Wait();

            timer.Stop();
            Console.WriteLine("CreateMultipleIndex finished. Elapsed time: " + timer.Elapsed);
        }
        private void PauseIndexWriting()
        {
            //UNDONE: PauseIndexWriting
        }
        private IndexReader OpenReader(string snapshot)
        {
            throw new NotImplementedException();
        }
        private IndexReader OpenWriterAndReader(string current)
        {
            throw new NotImplementedException();
        }
        private IndexReader OpenMultipleReader(IndexReader snapshotReader, IndexReader currentReader)
        {
            throw new NotImplementedException();
        }
        private void ContinueIndexWriting()
        {
            throw new NotImplementedException();
        }

        /* ======================================================================= COPY */
        private void CopyIndexFiles()
        {
            Console.WriteLine("CopyIndexFiles starts.");
            var timer = Stopwatch.StartNew();

            var source = _indexManager.IndexDirectory.CurrentDirectory;
            var target = _backupDirectoryPath;
            CopyFiles(source, target);

            timer.Stop();
            Console.WriteLine("CopyIndexFiles finished. Elapsed time: " + timer.Elapsed);
        }
        private void CopyFiles(string source, string target)
        {
            foreach (var filePath in Directory.GetFiles(source))
                if (!filePath.EndsWith("write.lock", StringComparison.InvariantCultureIgnoreCase))
                    File.Copy(filePath, Path.Combine(target, Path.GetFileName(filePath)));
        }

        /* ======================================================================= NOTIFY CALLER */
        private void NotifyCallerThatTheBackupIsComplete()
        {
            Console.WriteLine("NotifyCallerThatTheBackupIsComplete");
            //UNDONE: NotifyCallerThatTheBackupIsComplete
        }

        /* ======================================================================= MERGE */
        private void MergeIndexes()
        {
            Console.WriteLine("MergeIndexes starts.");
            var timer = Stopwatch.StartNew();

            //UNDONE: MergeIndexes
            Task.Delay(TimeSpan.FromSeconds(1.5)).Wait();

            timer.Stop();
            Console.WriteLine("MergeIndexes finished. Elapsed time: " + timer.Elapsed);
        }

        /* ======================================================================= NOTIFY SYSTEM */
        private void NotifySystemThatTheBackupIsComplete()
        {
            Console.WriteLine("NotifySystemThatTheBackupIsComplete");
            //UNDONE: NotifySystemThatTheBackupIsComplete
        }
    }
}
