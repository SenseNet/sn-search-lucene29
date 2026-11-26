using System;
using System.Linq;
using Lucene.Net.Index;

namespace SenseNet.Search.Lucene29
{
    internal class IndexSnapshot : IDisposable
    {
        private readonly SnapshotDeletionPolicy _snapshotMaker;

        public string[] FileNames { get; }
        public string SegmentFileName { get; }
        internal IndexCommitPoint CommitPoint { get; }

        public IndexSnapshot(SnapshotDeletionPolicy snapshotMaker)
        {
            _snapshotMaker = snapshotMaker;

            CommitPoint = _snapshotMaker.Snapshot();
            FileNames = CommitPoint.GetFileNames().ToArray();
            SegmentFileName = CommitPoint.GetSegmentsFileName();
        }
        public void Dispose()
        {
            _snapshotMaker.Release();
        }
    }
}
