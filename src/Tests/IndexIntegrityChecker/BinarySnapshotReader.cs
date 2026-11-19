using System.Text;

namespace IndexIntegrityChecker;

internal static class BinarySnapshotReader
{
    private const int HeaderSize = 16;

    public static IEnumerable<DatabaseVersion> ReadDatabase(string path, long rows, CancellationToken cancel)
    {
        using var reader = Open(path, rows, "SNICDB01", 32);
        for (long i = 0; i < rows; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var version = reader.ReadInt32(); var node = reader.ReadInt32();
            var versionTimestamp = reader.ReadInt64(); var nodeTimestamp = reader.ReadInt64();
            var major = reader.ReadInt32(); var minor = reader.ReadInt32();
            yield return new(version, versionTimestamp, node, nodeTimestamp, major == 0 ? null : major, minor == 0 ? null : minor);
        }
    }

    public static IEnumerable<SavedIndexVersion> ReadIndex(string path, long rows, int maxDoc, CancellationToken cancel)
    {
        using var reader = Open(path, rows, "SNICIX01", 40);
        for (long i = 0; i < rows; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var version = reader.ReadInt32(); var doc = reader.ReadInt32();
            var data = new IndexMetadata { NodeId = reader.ReadInt32(), DraftDocId = reader.ReadInt32(),
                NodeTimestamp = reader.ReadInt64(), VersionTimestamp = reader.ReadInt64(), Present = (Fields)reader.ReadByte() };
            var flags = reader.ReadByte(); reader.ReadInt16(); reader.ReadInt32();
            data.IsLastPublic = (flags & 1) != 0; data.IsLastDraft = (flags & 2) != 0;
            data.HistoricalPathMatches = (flags & 4) != 0;
            data.MissingOwnPath = (flags & 8) != 0; data.MissingDraftPath = (flags & 16) != 0;
            if (version <= 0 || doc < 0 || doc >= maxDoc || (flags & ~31) != 0 ||
                (data.MissingDraftPath && (data.DraftDocId < 0 || data.DraftDocId >= maxDoc)))
                throw new InvalidDataException("Invalid record in index snapshot.");
            yield return new(version, doc, data);
        }
    }

    private static BinaryReader Open(string path, long rows, string magic, int size)
    {
        var reader = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan), Encoding.UTF8);
        try
        {
            if (rows < 0 || reader.BaseStream.Length != checked(HeaderSize + rows * size) ||
                Encoding.ASCII.GetString(reader.ReadBytes(8)) != magic || reader.ReadInt32() != size || reader.ReadInt32() != 1)
                throw new InvalidDataException($"Unsupported or incomplete snapshot: {Path.GetFileName(path)}.");
            return reader;
        }
        catch { reader.Dispose(); throw; }
    }
}
