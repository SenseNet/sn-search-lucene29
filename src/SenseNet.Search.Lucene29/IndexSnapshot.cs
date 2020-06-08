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
}
