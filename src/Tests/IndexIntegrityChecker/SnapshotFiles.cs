using System.Globalization;
using System.Text;

namespace IndexIntegrityChecker;

/// <summary>UTF-8 tab-separated snapshots with a header; rows remain ordered by VersionId.</summary>
internal static class SnapshotFiles
{
    public const string DatabaseHeader = "nodeId\tversionId\tnodeTimestamp\tversionTimestamp\tlastMajorVersionId\tlastMinorVersionId";
    public const string IndexHeader = "nodeId\tversionId\tnodeTimestamp\tversionTimestamp\tisLastPublic\tisLastDraft\tdocId\tpresentFields\tdraftDocId\thistoricalPathMatches\tmissingOwnPath\tmissingDraftPath";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static StreamWriter StartDatabase(Stream stream) => CreateWriter(stream, DatabaseHeader);
    public static StreamWriter StartIndex(Stream stream) => CreateWriter(stream, IndexHeader);
    private static StreamWriter CreateWriter(Stream stream, string header)
    {
        var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024, leaveOpen: true) { NewLine = "\n" };
        writer.WriteLine(header);
        return writer;
    }

    public static void Write(StreamWriter writer, DatabaseVersion row) =>
        writer.WriteLine(string.Create(Invariant,
            $"{row.NodeId}\t{row.VersionId}\t{row.NodeTimestamp}\t{row.VersionTimestamp}\t{row.LastMajorVersionId}\t{row.LastMinorVersionId}"));

    public static void Write(StreamWriter writer, SavedIndexVersion row)
    {
        var data = row.Metadata;
        writer.WriteLine(string.Create(Invariant,
            $"{data.NodeId}\t{row.VersionId}\t{data.NodeTimestamp}\t{data.VersionTimestamp}\t{(data.IsLastPublic ? 1 : 0)}\t{(data.IsLastDraft ? 1 : 0)}\t{row.DocId}\t{(byte)data.Present}\t{data.DraftDocId}\t{(data.HistoricalPathMatches ? 1 : 0)}\t{(data.MissingOwnPath ? 1 : 0)}\t{(data.MissingDraftPath ? 1 : 0)}"));
    }

    public static IEnumerable<DatabaseVersion> ReadDatabase(string path, long rows, CancellationToken cancel) =>
        ReadRows(path, rows, DatabaseHeader, ParseDatabase, cancel);

    public static IEnumerable<SavedIndexVersion> ReadIndex(string path, long rows, int maxDoc, CancellationToken cancel) =>
        ReadRows(path, rows, IndexHeader, line => ParseIndex(line, maxDoc), cancel);

    private static DatabaseVersion ParseDatabase(string line)
    {
        var text = line.AsSpan();
        Span<Range> columns = stackalloc Range[7];
        if (text.Split(columns, '\t') != 6) throw new FormatException("Expected six columns.");
        var node = Int32(text[columns[0]]);
        var version = Int32(text[columns[1]]);
        if (version <= 0) throw new FormatException("VersionId must be positive.");
        return new(version, Int64(text[columns[3]]), node, Int64(text[columns[2]]),
            NullableInt32(text[columns[4]]), NullableInt32(text[columns[5]]));
    }

    private static SavedIndexVersion ParseIndex(string line, int maxDoc)
    {
        var text = line.AsSpan();
        Span<Range> columns = stackalloc Range[13];
        if (text.Split(columns, '\t') != 12) throw new FormatException("Expected twelve columns.");
        var node = Int32(text[columns[0]]);
        var version = Int32(text[columns[1]]);
        var doc = Int32(text[columns[6]]);
        var data = new IndexMetadata
        {
            NodeId = node, NodeTimestamp = Int64(text[columns[2]]), VersionTimestamp = Int64(text[columns[3]]),
            IsLastPublic = Boolean(text[columns[4]]), IsLastDraft = Boolean(text[columns[5]]),
            Present = (Fields)byte.Parse(text[columns[7]], NumberStyles.None, Invariant),
            DraftDocId = Int32(text[columns[8]]), HistoricalPathMatches = Boolean(text[columns[9]]),
            MissingOwnPath = Boolean(text[columns[10]]), MissingDraftPath = Boolean(text[columns[11]])
        };
        if (version <= 0 || doc < 0 || doc >= maxDoc ||
            (data.Present & ~(Fields.Metadata | Fields.VersionId)) != 0 ||
            (data.Present & Fields.VersionId) == 0 ||
            (data.MissingDraftPath && (data.DraftDocId < 0 || data.DraftDocId >= maxDoc)))
            throw new FormatException("Invalid index record.");
        return new(version, doc, data);
    }

    private static int Int32(ReadOnlySpan<char> value) => int.Parse(value, NumberStyles.AllowLeadingSign, Invariant);
    private static long Int64(ReadOnlySpan<char> value) => long.Parse(value, NumberStyles.AllowLeadingSign, Invariant);
    private static int? NullableInt32(ReadOnlySpan<char> value) => value.IsEmpty ? null : Int32(value);
    private static bool Boolean(ReadOnlySpan<char> value) => value switch
    {
        "0" => false,
        "1" => true,
        _ => throw new FormatException("Boolean values must be 0 or 1.")
    };

    private static IEnumerable<T> ReadRows<T>(string path, long rows, string header, Func<string, T> parse, CancellationToken cancel)
    {
        if (rows < 0) throw new InvalidDataException("Invalid snapshot row count.");
        cancel.ThrowIfCancellationRequested();
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        if (!string.Equals(reader.ReadLine(), header, StringComparison.Ordinal))
            throw new InvalidDataException($"Missing or invalid TSV header: {Path.GetFileName(path)}.");
        long count = 0;
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            var line = reader.ReadLine();
            if (line == null) break;
            if (count >= rows) throw new InvalidDataException($"Unexpected extra row in snapshot: {Path.GetFileName(path)}.");
            T row;
            try { row = parse(line); }
            catch (Exception error) when (error is FormatException or OverflowException)
            {
                throw new InvalidDataException($"Invalid TSV record at line {count + 2} in {Path.GetFileName(path)}.", error);
            }
            count++;
            yield return row;
        }
        if (count != rows) throw new InvalidDataException($"Incomplete snapshot: {Path.GetFileName(path)}. Expected {rows} rows, found {count}.");
    }
}
