using System.Threading;
using System.Threading.Tasks;
using SenseNet.Search.Indexing;

namespace SenseNet.Search.Lucene29
{
    public interface IBackupManagerFactory
    {
        IBackupManager CreateBackupManager();
    }

    public interface IBackupManager
    {
        BackupInfo BackupInfo { get; }
        Task BackupAsync(IndexingActivityStatus state, string backupDirectoryPath,
            LuceneSearchManager indexManager, CancellationToken cancellationToken);
    }
}
