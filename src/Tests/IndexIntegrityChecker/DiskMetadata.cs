using System.IO.MemoryMappedFiles;

namespace IndexIntegrityChecker;

/// <summary>Fixed-size, disk-backed metadata. Resident pages are managed by the OS.</summary>
internal sealed class DiskMetadata : IDisposable
{
    private const int RecordSize = 32;
    private readonly string _path;
    private readonly FileStream _file;
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    public int Count { get; }

    public DiskMetadata(int count, string? path = null)
    {
        Count = count;
        _path = path ?? Path.Combine(Path.GetTempPath(), $"index-integrity-{Guid.NewGuid():N}.map");
        _file = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        try
        {
            _file.SetLength(Math.Max(1, checked((long)count * RecordSize)));
            _mapping = MemoryMappedFile.CreateFromFile(_file, null, 0, MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None, leaveOpen: true);
            try { _view = _mapping.CreateViewAccessor(); }
            catch { _mapping.Dispose(); throw; }
        }
        catch { _file.Dispose(); File.Delete(_path); throw; }
    }

    public IndexMetadata this[int doc]
    {
        get
        {
            var offset = Offset(doc);
            var flags = _view.ReadByte(offset + 25);
            return new IndexMetadata
            {
                NodeId = _view.ReadInt32(offset), NodeTimestamp = _view.ReadInt64(offset + 8),
                VersionTimestamp = _view.ReadInt64(offset + 16), Present = (Fields)_view.ReadByte(offset + 24),
                IsLastPublic = (flags & 1) != 0, IsLastDraft = (flags & 2) != 0, IsCommit = (flags & 4) != 0,
                HistoricalPathMatches = (flags & 8) != 0, MissingOwnPath = (flags & 16) != 0,
                MissingDraftPath = (flags & 32) != 0, DraftPathReported = (flags & 64) != 0,
                DraftDocId = _view.ReadInt32(offset + 28)
            };
        }
        set
        {
            var offset = Offset(doc);
            _view.Write(offset, value.NodeId);
            _view.Write(offset + 8, value.NodeTimestamp);
            _view.Write(offset + 16, value.VersionTimestamp);
            _view.Write(offset + 24, (byte)value.Present);
            _view.Write(offset + 25, (byte)((value.IsLastPublic ? 1 : 0) | (value.IsLastDraft ? 2 : 0) |
                (value.IsCommit ? 4 : 0) | (value.HistoricalPathMatches ? 8 : 0) |
                (value.MissingOwnPath ? 16 : 0) | (value.MissingDraftPath ? 32 : 0) | (value.DraftPathReported ? 64 : 0)));
            _view.Write(offset + 28, value.DraftDocId);
        }
    }

    private long Offset(int doc)
    {
        if ((uint)doc >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(doc));
        return (long)doc * RecordSize;
    }

    public void Dispose()
    {
        _view.Dispose();
        _mapping.Dispose();
        _file.Dispose();
        File.Delete(_path);
    }
}
