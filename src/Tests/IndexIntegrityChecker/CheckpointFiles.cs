using System.Security.Cryptography;

namespace IndexIntegrityChecker;

internal sealed record SavedFile(string Name, long Length, long Rows, string Sha256);

/// <summary>A completed file is renamed into place only after its contents have been flushed to disk.</summary>
internal sealed class CheckpointWriter : IDisposable
{
    private readonly string _target;
    private readonly string _partial;
    private readonly FileStream _file;
    private readonly SHA256 _hash = SHA256.Create();
    private readonly CryptoStream _crypto;
    private readonly BufferedStream _stream;
    private bool _closed;
    public Stream Stream => _stream;

    public CheckpointWriter(string target)
    {
        _target = target;
        _partial = target + ".partial";
        _file = new FileStream(_partial, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);
        _crypto = new CryptoStream(_file, _hash, CryptoStreamMode.Write, leaveOpen: true);
        _stream = new BufferedStream(_crypto, 64 * 1024);
    }

    public SavedFile Complete(long rows, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        _stream.Flush();
        _crypto.FlushFinalBlock();
        _file.Flush(flushToDisk: true);
        var result = new SavedFile(Path.GetFileName(_target), _file.Length, rows, Convert.ToHexString(_hash.Hash!));
        Dispose();
        cancel.ThrowIfCancellationRequested();
        File.Move(_partial, _target, overwrite: true);
        return result;
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        try { _stream.Dispose(); }
        finally { _file.Dispose(); _hash.Dispose(); }
    }

    public static void AtomicWrite(string path, ReadOnlySpan<byte> data)
    {
        var partial = path + ".partial";
        using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(data);
            stream.Flush(flushToDisk: true);
        }
        File.Move(partial, path, overwrite: true);
    }

    public static async Task VerifyAsync(string directory, SavedFile saved, CancellationToken cancel)
    {
        if (saved.Name != Path.GetFileName(saved.Name) || string.IsNullOrWhiteSpace(saved.Name) || saved.Length < 0 || saved.Rows < 0)
            throw new InvalidDataException("Invalid artifact metadata in workflow.json.");
        var path = Path.Combine(directory, saved.Name);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        if (file.Length != saved.Length) throw new InvalidDataException($"Saved file has changed or is incomplete: {saved.Name}.");
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancel));
        if (!string.Equals(hash, saved.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException($"Checksum mismatch in saved file: {saved.Name}.");
    }
}

internal readonly record struct SavedIndexVersion(int VersionId, int DocId, IndexMetadata Metadata);

