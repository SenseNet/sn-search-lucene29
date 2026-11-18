using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IndexIntegrityChecker;

internal static class ComparisonSummaryUpgrade
{
    // TODO(refactor): Experimental saved activity enrichment and completed-repair trailer support; consolidate report readers later.
    public static void Run(WorkflowWorkspace workspace, WorkflowState state, TextWriter diagnostics, CancellationToken cancel)
    {
        var comparison = state.Comparison!;
        if (comparison.DifferentDocuments.HasValue && comparison.LastActivity != null) return;
        diagnostics.WriteLine("Updating the saved comparison summary...");
        var activity = comparison.LastActivity;
        var original = workspace.FilePath(comparison.Report.Name);
        var targetName = comparison.Report.Name == state.DefaultReportFile ? state.EnrichedReportFile : state.DefaultReportFile;
        using var output = new CheckpointWriter(workspace.FilePath(targetName));
        using var documents = new DifferentDocumentCounter(
            workspace.FilePath($"different-documents-{state.Id:N}.bits"), state.IndexSnapshot!.MaxDoc);
        var reporter = new DifferenceReporter(documents.Observe);
        var summaryFound = false;
        var repairFound = false;
        using (var reader = File.OpenText(original))
        using (var writer = new StreamWriter(output.Stream, new UTF8Encoding(false), 64 * 1024, leaveOpen: true))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                cancel.ThrowIfCancellationRequested();
                using var json = JsonDocument.Parse(line);
                if (summaryFound)
                {
                    var root = json.RootElement;
                    if (repairFound || state.Phase != WorkflowPhase.RepairCompleted || state.Repair == null ||
                        root.GetProperty("type").GetString() != "repair_completed" ||
                        root.GetProperty("workflowId").GetGuid() != state.Id ||
                        root.GetProperty("deletedDocuments").GetInt32() != state.Repair.DeletedDocuments ||
                        root.GetProperty("writtenVersions").GetInt32() != state.Repair.WrittenVersions ||
                        root.GetProperty("lastActivityId").GetInt32() != state.Repair.LastActivityId ||
                        root.GetProperty("gaps").GetArrayLength() != 0)
                        throw new InvalidDataException("Invalid or unexpected repair result after the report summary.");
                    writer.WriteLine(line);
                    repairFound = true;
                    continue;
                }
                switch (json.RootElement.GetProperty("type").GetString())
                {
                    case "difference":
                        reporter.Report(JsonSerializer.Deserialize<Difference>(line, JsonOutput.Options)
                            ?? throw new InvalidDataException("Invalid difference record."));
                        writer.WriteLine(line);
                        break;
                    case "summary":
                        if (reporter.Count != comparison.DifferenceCount ||
                            json.RootElement.GetProperty("differences").GetInt64() != reporter.Count)
                            throw new InvalidDataException("The report's difference count does not match its records.");
                        var summary = JsonNode.Parse(line)!.AsObject();
                        summary["differentDocuments"] = documents.Count;
                        activity ??= summary["lastActivity"]?.Deserialize<LastActivitySummary>(JsonOutput.Options)
                            ?? SavedComparison.ReadSnapshotActivity(state);
                        summary["lastActivity"] = JsonSerializer.SerializeToNode(activity, JsonOutput.Options);
                        writer.WriteLine(summary.ToJsonString(JsonOutput.Options));
                        summaryFound = true;
                        break;
                    default:
                        throw new InvalidDataException("Unknown report record type.");
                }
            }
            if (!summaryFound) throw new InvalidDataException("The report summary is missing.");
            if (comparison.Report.Rows != reporter.Count + 1 + (repairFound ? 1 : 0))
                throw new InvalidDataException("The report row count does not match its records.");
        }
        var saved = output.Complete(reporter.Count + 1 + (repairFound ? 1 : 0), cancel);
        state.Comparison = comparison with { Report = saved, DifferentDocuments = documents.Count, LastActivity = activity };
        workspace.Save(state);
        // The completed report remains recoverable if writing the replacement or its state fails.
        try { File.Delete(original); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { diagnostics.WriteLine($"Summary saved; could not remove unused report: {Path.GetFileName(original)}."); }
    }
}
