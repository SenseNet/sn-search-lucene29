using System.Globalization;
using Microsoft.Data.SqlClient;

namespace IndexIntegrityChecker;

// TODO(refactor): Experimental repair-enabled CLI; finalize interactive workflow integration later.
public static class Program
{
    // TODO(refactor): Experimental repair wiring; finalize dependency construction after the experiment.
    public static async Task<int> Main(string[] args)
    {
        if (!CommandLineOptions.TryParse(args, out var options, out var argumentError))
        {
            Console.Error.WriteLine(argumentError);
            CommandLineOptions.WriteUsage(Console.Error);
            return 2;
        }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            return await ExecuteAsync(options, new WorkflowOperations(Console.Error), new WindowsConnectionProtector(),
                Console.Out, Console.Error, cancellation.Token, ComparisonRepairer.OpenAsync,
                (_, summary, cancel) => ConfirmRepairAsync(summary, Console.In, Console.Error, !Console.IsInputRedirected, cancel));
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    // TODO(refactor): Experimental repair result/error routing; consolidate CLI outcomes after validation.
    internal static async Task<int> ExecuteAsync(CommandLineOptions options, IWorkflowOperations operations,
        IConnectionProtector protector, TextWriter output, TextWriter diagnostics, CancellationToken cancel,
        Func<string, IndexIdentity, CancellationToken, Task<ComparisonRepairer>>? repairFactory = null,
        Func<IndexIdentity, RepairResult, CancellationToken, Task<bool>>? confirmRepair = null)
    {
        var persistent = options.WorkingDirectory != null;
        TemporaryWorkingDirectory? temporary = null;
        var cleanupFailed = false;
        WorkflowRunner? runner = null;
        WorkflowState? returnedState = null;
        int exitCode;
        try
        {
            if (!persistent) temporary = new TemporaryWorkingDirectory();
            var directory = persistent ? Path.GetFullPath(options.WorkingDirectory!) : temporary!.DirectoryPath;
            diagnostics.WriteLine(persistent ? "Mode: persistent (resumable)." : "Mode: temporary (no persistence; not resumable).");
            var displayedDifferences = 0;
            var limitReported = false;
            void WriteDifference(string line)
            {
                if (displayedDifferences < options.MaxConsoleDifferences)
                {
                    output.WriteLine(line);
                    displayedDifferences++;
                }
                else if (!limitReported)
                {
                    diagnostics.WriteLine($"Only the first {options.MaxConsoleDifferences} differences are listed. " +
                        "The check continues; the final summary will include all differences if the check completes. " +
                        "Rerun with -w \"<directory>\" to retain all differences.");
                    limitReported = true;
                }
            }

            var summaryWritten = false;
            // TODO(refactor): Experimental live diagnostics before display; consolidate source credentials and summary lifetimes later.
            async Task<LastActivitySummary> ReadActivity(WorkflowState completed)
            {
                var connection = persistent
                    ? protector.Unprotect(File.ReadAllBytes(Path.Combine(directory, completed.CredentialsFile)), completed.Id)
                    : options.ConnectionString ?? throw new InvalidOperationException("SQL connection settings are missing.");
                try { return await operations.ReadActivityAsync(connection, completed.Index.Directory, cancel); }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                { throw new IOException("Cannot read LAST ACTIVITY: " + SanitizeError(error, connection)); }
            }
            // TODO(refactor): Experimental comparison display callback; consolidate console reporting after the workflow API stabilizes.
            Task WriteSummary(WorkflowState completed)
            {
                WriteComparisonSummary(output, completed);
                summaryWritten = true;
                return Task.CompletedTask;
            }
            runner = new WorkflowRunner(operations, protector, diagnostics, repairFactory: repairFactory, confirmRepair: confirmRepair);
            var state = await runner.RunAsync(options with { WorkingDirectory = directory }, cancel, persistent,
                persistent ? null : WriteDifference, repairFactory == null ? null : WriteSummary);
            returnedState = state;
            var result = state.Comparison!;
            if (!summaryWritten) await WriteSummary(state);
            if (state.Phase == WorkflowPhase.RepairCompleted)
                WriteRepairSummary(output, state.Repair!, await ReadActivity(state));
            exitCode = state.Phase == WorkflowPhase.RepairCompleted ? 0 : state.Phase == WorkflowPhase.RepairPrepared ? 1 : result.DifferenceCount == 0 ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            diagnostics.WriteLine(returnedState?.Phase == WorkflowPhase.RepairCompleted
                ? "Repair completed, but displaying its result was cancelled."
                : runner?.RepairWasStarted == true
                ? "Repair did not complete. Run a new full check; the previous comparison cannot be resumed or reapplied."
                : persistent
                ? "Cancelled. Resume from the last completed checkpoint using -w only."
                : "Cancelled. Temporary runs cannot be resumed.");
            exitCode = 130;
        }
        catch (Exception error)
        {
            diagnostics.WriteLine("Check failed: " + SanitizeError(error, options.ConnectionString));
            diagnostics.WriteLine(returnedState?.Phase == WorkflowPhase.RepairCompleted
                ? "Repair completed, but displaying its result failed."
                : runner?.RepairWasStarted == true
                ? "Repair did not complete. Run a new full check; the previous comparison cannot be resumed or reapplied."
                : persistent
                ? "Any completed checkpoints are preserved. Resume using -w only after resolving the error."
                : "Temporary runs cannot be resumed. Start a new check after resolving the error.");
            exitCode = 2;
        }
        finally
        {
            if (temporary != null)
            {
                try
                {
                    temporary.Dispose();
                    diagnostics.WriteLine("Temporary working directory removed.");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    cleanupFailed = true;
                    diagnostics.WriteLine($"Could not remove temporary working directory '{temporary.DirectoryPath}': {error.Message}");
                }
            }
        }
        return cleanupFailed && exitCode is 0 or 1 ? 2 : exitCode;
    }

    // TODO(refactor): Experimental readable console summary; consolidate presentation separately from the saved JSONL report.
    internal static void WriteComparisonSummary(TextWriter output, WorkflowState state)
    {
        var comparison = state.Comparison ?? throw new InvalidOperationException("Comparison is not complete.");
        var activity = comparison.LastActivity ?? throw new InvalidDataException("The comparison summary has no saved activity state.");
        var counts = new (string Label, long? Value)[]
        {
            ("  databaseVersions:", comparison.Counts.DatabaseVersions),
            ("  indexDocuments:", comparison.Counts.IndexDocuments),
            ("  matchedDocuments:", comparison.Counts.MatchedDocuments),
            ("  differences:", comparison.DifferenceCount),
            ("  differentDocuments:", comparison.DifferentDocuments)
        };
        var kinds = comparison.DifferenceCount == 0 ? [] : comparison.CountsByKind
            .Where(pair => pair.Value > 0).Select(pair => ($"    {pair.Key}:", (long?)pair.Value)).ToArray();
        WriteSummaryBlock(output, "COMPARISON SUMMARY", comparison.DifferenceCount == 0 ? "consistent" : "differences",
            counts, kinds, activity);
    }

    // TODO(refactor): Experimental readable repair result; consolidate console presentation with the persisted completion record later.
    internal static void WriteRepairSummary(TextWriter output, RepairResult repair, LastActivitySummary liveActivity) =>
        WriteSummaryBlock(output, "REPAIR SUMMARY", "completed",
            [("  deletedDocuments:", repair.DeletedDocuments), ("  writtenVersions:", repair.WrittenVersions)], [], liveActivity);

    // TODO(refactor): Experimental shared summary layout; extract a supported console presentation API after validation.
    private static void WriteSummaryBlock(TextWriter output, string heading, string status,
        (string Label, long? Value)[] counts, (string Label, long? Value)[] kinds, LastActivitySummary liveActivity)
    {
        ArgumentNullException.ThrowIfNull(liveActivity);
        var activity = liveActivity.Index;
        ArgumentNullException.ThrowIfNull(activity);
        var labelWidth = Math.Max(31, kinds.Select(pair => pair.Item1.Length + 1).DefaultIfEmpty(0).Max());
        var numberWidth = Math.Max(4, counts.Select(pair => pair.Value).Concat(kinds.Select(pair => pair.Item2))
            .Concat([liveActivity.Database, activity.LastActivityId])
            .Select(value => value?.ToString(CultureInfo.InvariantCulture).Length ?? 0).Max());
        output.WriteLine(heading + ":");
        output.WriteLine("  status:".PadRight(labelWidth) + status);
        foreach (var (label, value) in counts) WriteNumber(label, value);
        if (kinds.Length > 0)
        {
            output.WriteLine("  COUNTS BY KIND:");
            foreach (var (label, value) in kinds) WriteNumber(label, value);
        }
        output.WriteLine("  LAST ACTIVITY:");
        WriteNumber("    database:", liveActivity.Database);
        var gaps = activity is { GapCount: > 0 }
            ? " (" + string.Join(", ", activity.GapPreview.Select(id => id.ToString(CultureInfo.InvariantCulture)))
                + (activity.GapCount > 5 ? $", ... {activity.GapCount.ToString(CultureInfo.InvariantCulture)} total" : "") + ")"
            : "";
        WriteNumber("    index:", activity.LastActivityId, gaps);
        output.WriteLine(new string('-', Math.Max(47, labelWidth + numberWidth)));

        // TODO(refactor): Experimental column layout; share with the final console summary formatter later.
        void WriteNumber(string label, long? value, string suffix = "") =>
            output.WriteLine(label.PadRight(labelWidth)
                + (value?.ToString(CultureInfo.InvariantCulture) ?? "unavailable").PadLeft(numberWidth) + suffix);
    }

    // TODO(refactor): Experimental interactive confirmation; finalize UI contract after the experiment.
    internal static async Task<bool> ConfirmRepairAsync(RepairResult summary, TextReader input,
        TextWriter diagnostics, bool interactive, CancellationToken cancel)
    {
        diagnostics.WriteLine($"Repair: {summary.WrittenVersions} versions to write, {summary.DeletedDocuments} documents to delete.");
        diagnostics.WriteLine($"Final activity state: LastActivityId = {summary.LastActivityId}, Gaps = empty.");
        if (!interactive) { diagnostics.WriteLine("Non-interactive input: repair requires manual confirmation."); return false; }
        diagnostics.Write("Perform the repair? [yes/NO] ");
        var answer = await input.ReadLineAsync(cancel);
        return answer?.Trim().ToLowerInvariant() is "yes" or "y";
    }

    internal static string SanitizeError(Exception error, string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return error.Message;
        var message = error.Message.Replace(connectionString, "[connection string redacted]", StringComparison.Ordinal);
        try
        {
            var password = new SqlConnectionStringBuilder(connectionString).Password;
            if (!string.IsNullOrEmpty(password)) message = message.Replace(password, "[redacted]", StringComparison.Ordinal);
        }
        catch (ArgumentException) { return "Invalid connection string or input. No connection-string details are logged."; }
        return message;
    }
}
