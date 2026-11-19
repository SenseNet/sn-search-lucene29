using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndexIntegrityChecker;

public readonly record struct DatabaseVersion(int VersionId, long VersionTimestamp, int NodeId,
    long NodeTimestamp, int? LastMajorVersionId, int? LastMinorVersionId);

public readonly record struct IndexVersion(int VersionId, int DocId);

public enum DifferenceKind
{
    NotInIndex, NotInDatabase, MoreDocument, DifferentNodeId, DifferentNodeTimestamp,
    DifferentVersionTimestamp, DifferentLastPublicFlag, DifferentLastDraftFlag,
    MissingField, InvalidField, MultipleFieldValues
}

public sealed record Difference(DifferenceKind Kind, int? VersionId = null, int? DocId = null,
    int? NodeId = null, string? Field = null, object? Expected = null, object? Actual = null);

public sealed class DifferenceReporter(Action<Difference> write)
{
    public long Count { get; private set; }
    public Dictionary<DifferenceKind, long> Counts { get; } = new();
    public void Report(Difference difference)
    {
        Count++;
        Counts[difference.Kind] = Counts.GetValueOrDefault(difference.Kind) + 1;
        write(difference);
    }
}

public static class JsonOutput
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}

[Flags]
internal enum Fields : byte
{
    None = 0, NodeId = 1, NodeTimestamp = 2, VersionTimestamp = 4,
    IsLastPublic = 8, IsLastDraft = 16, VersionId = 32,
    Metadata = NodeId | NodeTimestamp | VersionTimestamp | IsLastPublic | IsLastDraft
}

internal struct IndexMetadata
{
    public int NodeId;
    public long NodeTimestamp, VersionTimestamp;
    public Fields Present;
    public bool IsLastPublic, IsLastDraft;
    public bool IsCommit, HistoricalPathMatches, MissingOwnPath, MissingDraftPath, DraftPathReported;
    public int DraftDocId;
}
