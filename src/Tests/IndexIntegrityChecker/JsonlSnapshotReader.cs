using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndexIntegrityChecker;

/// <summary>One UTF-8 JSON object per line; rows are streamed in VersionId order.</summary>
internal static class JsonlSnapshotReader
{
    private static readonly JsonSerializerOptions Options = new(JsonOutput.Options)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static IEnumerable<DatabaseVersion> ReadDatabase(string path, long rows, CancellationToken cancel)
    {
        foreach (var row in ReadRows<DatabaseRow>(path, rows, cancel))
        {
            if (row.VersionId <= 0) throw new InvalidDataException("Invalid VersionId in database snapshot.");
            yield return new(row.VersionId, row.VersionTimestamp, row.NodeId, row.NodeTimestamp,
                row.LastMajorVersionId, row.LastMinorVersionId);
        }
    }

    public static IEnumerable<SavedIndexVersion> ReadIndex(string path, long rows, int maxDoc, CancellationToken cancel)
    {
        foreach (var row in ReadRows<IndexRow>(path, rows, cancel))
        {
            if (row.VersionId <= 0 || row.DocId < 0 || row.DocId >= maxDoc ||
                (row.PresentFields & ~(Fields.Metadata | Fields.VersionId)) != 0 ||
                (row.PresentFields & Fields.VersionId) == 0 ||
                (row.MissingDraftPath && (row.DraftDocId < 0 || row.DraftDocId >= maxDoc)))
                throw new InvalidDataException("Invalid record in index snapshot.");
            yield return new(row.VersionId, row.DocId, new IndexMetadata
            {
                NodeId = row.NodeId, NodeTimestamp = row.NodeTimestamp, VersionTimestamp = row.VersionTimestamp,
                IsLastPublic = row.IsLastPublic, IsLastDraft = row.IsLastDraft, Present = row.PresentFields,
                DraftDocId = row.DraftDocId, HistoricalPathMatches = row.HistoricalPathMatches,
                MissingOwnPath = row.MissingOwnPath, MissingDraftPath = row.MissingDraftPath
            });
        }
    }

    private static IEnumerable<T> ReadRows<T>(string path, long rows, CancellationToken cancel) where T : class
    {
        if (rows < 0) throw new InvalidDataException("Invalid snapshot row count.");
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        long count = 0;
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            var line = reader.ReadLine();
            if (line == null) break;
            if (count >= rows) throw new InvalidDataException($"Unexpected extra row in snapshot: {Path.GetFileName(path)}.");
            T row;
            try
            {
                row = JsonSerializer.Deserialize<T>(line, Options) ?? throw new JsonException("A JSON object is required.");
            }
            catch (JsonException error)
            {
                throw new InvalidDataException($"Invalid JSONL record at line {count + 1} in {Path.GetFileName(path)}.", error);
            }
            count++;
            yield return row;
        }
        if (count != rows) throw new InvalidDataException($"Incomplete snapshot: {Path.GetFileName(path)}. Expected {rows} rows, found {count}.");
    }

    private sealed record DatabaseRow(
        [property: JsonRequired] int VersionId,
        [property: JsonRequired] long VersionTimestamp,
        [property: JsonRequired] int NodeId,
        [property: JsonRequired] long NodeTimestamp,
        [property: JsonRequired] int? LastMajorVersionId,
        [property: JsonRequired] int? LastMinorVersionId);

    private sealed record IndexRow(
        [property: JsonRequired] int VersionId,
        [property: JsonRequired] int DocId,
        [property: JsonRequired] int NodeId,
        [property: JsonRequired] long NodeTimestamp,
        [property: JsonRequired] long VersionTimestamp,
        [property: JsonRequired] bool IsLastPublic,
        [property: JsonRequired] bool IsLastDraft,
        [property: JsonRequired] Fields PresentFields,
        [property: JsonRequired] int DraftDocId,
        [property: JsonRequired] bool HistoricalPathMatches,
        [property: JsonRequired] bool MissingOwnPath,
        [property: JsonRequired] bool MissingDraftPath);
}
