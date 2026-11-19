using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucene.Net.Documents;

namespace IndexIntegrityChecker;

// TODO(refactor): Experimental repair summary contract; finalize before exposing a supported repair API.
internal sealed record RepairResult(int DeletedDocuments, int WrittenVersions, int LastActivityId);

// TODO(refactor): Experimental repair orchestrator; consolidate into a supported offline workflow after validation.
internal sealed class ComparisonRepairer : IAsyncDisposable
{
    private readonly LuceneRepairWriter _index;
    private readonly RepairSchema _schema;
    private readonly Func<int, CancellationToken, Task<RepairVersion?>> _load;
    private readonly Func<CancellationToken, Task<int>> _activity;
    private readonly Func<ValueTask>? _disposeSource;
    private readonly Action<string>? _progress;
    private readonly HashSet<int> _deletes = [];
    private readonly HashSet<int> _writes = [];
    private readonly Dictionary<int, string> _fingerprints = [];
    private int _lastActivity;
    private bool _prepared;

    // TODO(refactor): Experimental preview contract; finalize together with workflow persistence.
    internal RepairResult Summary => new(_deletes.Count, _writes.Count, _lastActivity);

    // TODO(refactor): Experimental summary enrichment from the original commit; consolidate activity snapshot APIs later.
    internal IndexActivitySummary OriginalIndexActivity => IndexActivitySummary.FromUserData(_index.OriginalUserData);

    // TODO(refactor): Experimental read-only activity access for prepared workflows; consolidate with source snapshot APIs later.
    internal async Task<int> ReadLastActivityIdAsync(CancellationToken cancel)
    {
        var last = await _activity(cancel);
        if (last < 0) throw new InvalidDataException("Invalid last database activity id.");
        return last;
    }

    // TODO(refactor): Experimental MSSQL/Lucene composition; replace with a supported offline session later.
    internal static async Task<ComparisonRepairer> OpenAsync(string connection, IndexIdentity index, CancellationToken cancel)
    {
        var source = await MsSqlRepairSource.OpenAsync(connection, cancel);
        try { return new(index, source.Schema, source.ReadVersionAsync, source.GetLastIndexingActivityIdAsync, source.DisposeAsync); }
        catch { await source.DisposeAsync(); throw; }
    }

    // TODO(refactor): Experimental constructor; retain delegates as test seams until the repair API is finalized.
    internal ComparisonRepairer(IndexIdentity index, RepairSchema schema,
        Func<int, CancellationToken, Task<RepairVersion?>> load, Func<CancellationToken, Task<int>> activity,
        Func<ValueTask>? disposeSource = null, Action<string>? progress = null)
    {
        _schema = schema; _load = load; _activity = activity; _disposeSource = disposeSource; _progress = progress;
        _index = new(index);
    }

    // TODO(refactor): Experimental comparison preparation; refactor its contract after repair validation.
    internal async Task<bool> PrepareAsync(string comparisonPath, CancellationToken cancel)
    {
        if (_prepared) throw new InvalidOperationException("This repair session is already prepared.");
        var differences = ReadReport(comparisonPath, cancel);
        var versions = new HashSet<int>();
        foreach (var difference in differences)
        {
            if (difference.DocId is { } doc) _deletes.Add(doc);
            if (difference.VersionId is { } version) versions.Add(version);
        }
        _index.Expand(_deletes, versions, cancel);
        var byVersion = differences.Where(d => d.VersionId.HasValue).ToLookup(d => d.VersionId!.Value);
        foreach (var version in versions)
        {
            cancel.ThrowIfCancellationRequested();
            var row = await _load(version, cancel);
            var expected = row is { Excluded: false };
            foreach (var difference in byVersion[version]) ValidateExpected(difference, row, expected);
            if (!expected) continue;
            var document = RepairDocuments.Convert(row!, _schema);
            using var analyzer = _schema.CreateAnalyzer(document.GetFields().Cast<Fieldable>().Select(f => f.Name()));
            _writes.Add(version);
            _fingerprints.Add(version, Fingerprint(row!));
        }
        _lastActivity = await ReadLastActivityIdAsync(cancel);
        _index.ValidateCommit();
        _prepared = true;
        var data = _index.OriginalUserData;
        var activityChanged = !data.TryGetValue("LastActivityId", out var last) || last != _lastActivity.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || data.ContainsKey("Gaps");
        return _deletes.Count != 0 || _writes.Count != 0 || activityChanged;
    }

    // TODO(refactor): Experimental source freshness validation; move into the supported report contract later.
    private static void ValidateExpected(Difference difference, RepairVersion? row, bool expected)
    {
        if (difference.Kind == DifferenceKind.NotInDatabase)
        {
            if (expected) throw new InvalidDataException("SQL indexing eligibility changed since comparison. Run a new full check.");
            return;
        }
        if (difference.Kind is DifferenceKind.MoreDocument or DifferenceKind.MissingField or DifferenceKind.InvalidField or DifferenceKind.MultipleFieldValues) return;
        if (!expected) throw new InvalidDataException($"SQL version {difference.VersionId} changed or disappeared. Run a new full check.");
        object? actual = difference.Kind switch
        {
            DifferenceKind.NotInIndex => row!.NodeId,
            DifferenceKind.DifferentNodeId => row!.NodeId,
            DifferenceKind.DifferentNodeTimestamp => row!.NodeTimestamp,
            DifferenceKind.DifferentVersionTimestamp => row!.VersionTimestamp,
            DifferenceKind.DifferentLastPublicFlag => row!.LastPublic == row.VersionId,
            DifferenceKind.DifferentLastDraftFlag => row!.LastDraft == row.VersionId,
            _ => null
        };
        var recorded = difference.Kind == DifferenceKind.NotInIndex ? (object?)difference.NodeId : difference.Expected;
        if (recorded == null || JsonSerializer.Serialize(actual) != JsonSerializer.Serialize(recorded))
            throw new InvalidDataException($"SQL metadata changed for version {difference.VersionId}. Run a new full check.");
    }

    // TODO(refactor): Experimental streaming report validation; consolidate with the checker report reader later.
    private static List<Difference> ReadReport(string path, CancellationToken cancel)
    {
        var differences = new List<Difference>();
        var counts = new Dictionary<DifferenceKind, long>();
        var summary = false;
        foreach (var line in File.ReadLines(path))
        {
            cancel.ThrowIfCancellationRequested();
            if (summary) throw new InvalidDataException("Unexpected data after comparison summary.");
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "difference":
                    var difference = JsonSerializer.Deserialize<Difference>(line, JsonOutput.Options) ?? throw new InvalidDataException("Invalid comparison difference.");
                    if (!Enum.IsDefined(difference.Kind) || difference.VersionId is <= 0 || difference.DocId is < 0 ||
                        (!difference.VersionId.HasValue && !difference.DocId.HasValue) ||
                        (difference.Kind == DifferenceKind.NotInIndex && !difference.VersionId.HasValue) ||
                        (difference.Kind != DifferenceKind.NotInIndex && !difference.DocId.HasValue))
                        throw new InvalidDataException("Invalid comparison target.");
                    differences.Add(difference);
                    counts[difference.Kind] = counts.GetValueOrDefault(difference.Kind) + 1;
                    break;
                case "summary":
                    if (root.GetProperty("differences").GetInt64() != differences.Count) throw new InvalidDataException("Comparison difference count mismatch.");
                    var reported = root.GetProperty("countsByKind").Deserialize<Dictionary<DifferenceKind, long>>(JsonOutput.Options)!;
                    if (reported.Count != counts.Count || counts.Any(kv => reported.GetValueOrDefault(kv.Key) != kv.Value))
                        throw new InvalidDataException("Comparison countsByKind mismatch.");
                    summary = true;
                    break;
                default: throw new InvalidDataException("Unknown comparison record.");
            }
        }
        if (!summary) throw new InvalidDataException("Comparison has no final summary.");
        return differences;
    }

    // TODO(refactor): Experimental prewrite guard; integrate with a supported mutation boundary.
    internal void ValidateBeforeStart() => _index.ValidateCommit();

    // TODO(refactor): Experimental non-resumable repair entry; finalize contract after the experiment.
    internal async Task<RepairResult> ApplyAsync(CancellationToken cancel)
    {
        if (!_prepared) throw new InvalidOperationException("Repair has not been prepared.");
        await _index.ApplyAsync(_deletes, _writes, LoadDocumentAsync, _schema, ValidateActivityAsync, _lastActivity, cancel, _progress);
        return Summary;
    }

    // TODO(refactor): Experimental bounded payload reload; share validated offline document loading later.
    private async Task<Document> LoadDocumentAsync(int version, CancellationToken cancel)
    {
        var row = await _load(version, cancel);
        if (row == null || row.Excluded || Fingerprint(row) != _fingerprints[version])
            throw new InvalidDataException($"SQL version {version} changed after preparation. Run a new full check.");
        return RepairDocuments.Convert(row, _schema);
    }

    // TODO(refactor): Experimental immutable-row guard; finalize a supported source fingerprint contract later.
    private static string Fingerprint(RepairVersion row) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row))));

    // TODO(refactor): Experimental activity boundary check; integrate into supported offline commit semantics.
    private async Task ValidateActivityAsync(CancellationToken cancel)
    {
        if (await _activity(cancel) != _lastActivity) throw new InvalidDataException("Database activity id changed during repair. Run a new full check.");
    }

    // TODO(refactor): Experimental resource ownership; finalize the repair session lifecycle.
    public async ValueTask DisposeAsync()
    {
        try { _index.Dispose(); }
        finally { if (_disposeSource != null) await _disposeSource(); }
    }
}
