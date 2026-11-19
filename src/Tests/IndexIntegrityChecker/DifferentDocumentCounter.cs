namespace IndexIntegrityChecker;

/// <summary>Distinct indexed DocIds plus ordered SQL versions missing from the index.</summary>
internal sealed class DifferentDocumentCounter(string path, int maxDoc) : IDisposable
{
    private readonly DiskBitSet _documents = new(path, maxDoc);
    private int _lastMissingVersion;
    public long Count { get; private set; }

    public void Observe(Difference difference)
    {
        if (difference.Kind == DifferenceKind.NotInIndex)
        {
            if (difference.DocId != null || difference.VersionId is not > 0 ||
                difference.VersionId < _lastMissingVersion)
                throw new InvalidDataException("Missing SQL versions must have positive, ordered VersionIds and no DocId.");
            if (difference.VersionId != _lastMissingVersion)
            {
                Count++;
                _lastMissingVersion = difference.VersionId.Value;
            }
        }
        else if (difference.DocId is int doc)
        {
            if (_documents.TryMark(doc)) Count++;
        }
        else throw new InvalidDataException("An index difference must identify its DocId.");
    }

    public void Dispose() => _documents.Dispose();
}
