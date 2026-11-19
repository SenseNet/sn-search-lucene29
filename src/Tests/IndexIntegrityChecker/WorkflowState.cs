using System.Globalization;

namespace IndexIntegrityChecker;

// TODO(refactor): Experimental repair phases; finalize persisted lifecycle before production use.
internal enum WorkflowPhase { Initialized, DatabaseSaved, IndexSaved, ComparisonSaved, Completed, RepairPrepared, RepairStarted, RepairCompleted }

internal sealed record IndexIdentity(string Directory, long CommitVersion);
// TODO(refactor): Experimental comparison activity snapshot and live repair display contract; consolidate the diagnostic API later.
internal sealed record LastActivitySummary(int Database, IndexActivitySummary Index);
// TODO(refactor): Experimental activity snapshot extension; consolidate with the offline export contract later.
internal sealed record DatabaseExport(SavedFile Data, int ExcludedTypes, string Isolation, int? LastActivityId = null);
// TODO(refactor): Experimental commit activity snapshot; retain a bounded preview in the supported export contract.
internal sealed record IndexExport(SavedFile Data, SavedFile Issues, int MaxDoc, int ContentDocuments,
    IndexActivitySummary? Activity = null);

// TODO(refactor): Experimental display snapshot; consolidate with a supported activity-state reader later.
internal sealed record IndexActivitySummary(int LastActivityId, int[] GapPreview, int GapCount)
{
    // TODO(refactor): Local adaptation of sn-search-lucene29@a90399d7599b090c92781dedefa3c9fd657adaf6
    // src/SenseNet.Search.Lucene29/LuceneSearchManager.cs::ParseFromReader.
    // Adaptation: retain only five valid gap ids plus the total count for display and persistence.
    internal static IndexActivitySummary FromUserData(IDictionary<string, string>? data)
    {
        var last = 0;
        if (data != null && data.TryGetValue("LastActivityId", out var value))
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out last);
        var preview = new List<int>(5);
        var count = 0;
        if (data != null && data.TryGetValue("Gaps", out var gaps) && !string.IsNullOrWhiteSpace(gaps))
            foreach (var token in gaps.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
                {
                    count++;
                    if (preview.Count < 5) preview.Add(id);
                }
        return new(last, preview.ToArray(), count);
    }
}
// TODO(refactor): Experimental saved comparison activity; consolidate the report and checkpoint summary contracts later.
internal sealed record ComparisonExport(SavedFile Report, CheckCounts Counts, long DifferenceCount,
    Dictionary<DifferenceKind, long> CountsByKind, long ElapsedMilliseconds, long? DifferentDocuments = null,
    LastActivitySummary? LastActivity = null);

// TODO(refactor): Experimental repair state extension; finalize the completion/preview contract later.
internal sealed class WorkflowState
{
    public const int CurrentFormatVersion = 3;
    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public WorkflowPhase Phase { get; set; }
    public string OriginalIndexPath { get; set; } = "";
    public IndexIdentity Index { get; set; } = new("", 0);
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DatabaseExport? Database { get; set; }
    public IndexExport? IndexSnapshot { get; set; }
    public ComparisonExport? Comparison { get; set; }
    // TODO(refactor): Experimental preview/result persistence; consolidate with the final workflow schema.
    public RepairResult? Repair { get; set; }
    private string SnapshotExtension => FormatVersion switch { 1 => "bin", 2 => "jsonl", _ => "tsv" };
    public string DatabaseFile => $"database-{Id:N}.{SnapshotExtension}";
    public string IndexFile => $"index-{Id:N}.{SnapshotExtension}";
    public string IssuesFile => $"index-issues-{Id:N}.jsonl";
    internal string DefaultReportFile => $"comparison-{Id:N}.jsonl";
    internal string EnrichedReportFile => $"comparison-{Id:N}-summary.jsonl";
    public string ReportFile => Comparison?.Report?.Name ?? DefaultReportFile;
    public string CredentialsFile => $"connection-{Id:N}.dpapi";
    public string ScratchFile => $"index-scratch-{Id:N}.map";

    // TODO(refactor): Experimental repair-state validation; consolidate state transitions after validation.
    public void Validate()
    {
        if (FormatVersion is not (1 or 2 or CurrentFormatVersion) || Id == Guid.Empty || !Enum.IsDefined(Phase) ||
            string.IsNullOrWhiteSpace(OriginalIndexPath) || !Path.IsPathFullyQualified(OriginalIndexPath) ||
            Index == null || string.IsNullOrWhiteSpace(Index.Directory) || !Path.IsPathFullyQualified(Index.Directory))
            throw new InvalidDataException("Unsupported or invalid workflow.json.");
        if (Phase >= WorkflowPhase.DatabaseSaved && (Database?.Data == null || Database.Data.Name != DatabaseFile))
            throw new InvalidDataException("Missing or invalid database checkpoint.");
        if (Phase >= WorkflowPhase.IndexSaved && (IndexSnapshot?.Data == null || IndexSnapshot.Issues == null ||
            IndexSnapshot.Data.Name != IndexFile || IndexSnapshot.Issues.Name != IssuesFile ||
            IndexSnapshot.MaxDoc < 0 || IndexSnapshot.ContentDocuments < 0 || IndexSnapshot.ContentDocuments > IndexSnapshot.MaxDoc))
            throw new InvalidDataException("Missing or invalid index checkpoint.");
        if (Phase >= WorkflowPhase.ComparisonSaved && (Comparison?.Report == null || Comparison.CountsByKind == null ||
            (Comparison.Report.Name != DefaultReportFile && Comparison.Report.Name != EnrichedReportFile) ||
            Comparison.DifferenceCount < 0 || Comparison.DifferentDocuments is < 0 ||
            Comparison.DifferentDocuments > Comparison.DifferenceCount))
            throw new InvalidDataException("Missing or invalid comparison checkpoint.");
        if (Phase >= WorkflowPhase.RepairPrepared &&
            (Repair == null || Repair.DeletedDocuments < 0 || Repair.WrittenVersions < 0 || Repair.LastActivityId < 0))
            throw new InvalidDataException("Missing or invalid repair summary.");
    }
}
