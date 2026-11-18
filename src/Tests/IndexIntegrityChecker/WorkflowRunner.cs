using Microsoft.Data.SqlClient;

namespace IndexIntegrityChecker;

// TODO(refactor): Experimental repair composition; finalize callback and session ownership contracts later.
internal sealed class WorkflowRunner(IWorkflowOperations operations, IConnectionProtector protector,
    TextWriter diagnostics, Action<WorkflowPhase>? checkpointSaved = null,
    Func<string, IndexIdentity, CancellationToken, Task<ComparisonRepairer>>? repairFactory = null,
    Func<IndexIdentity, RepairResult, CancellationToken, Task<bool>>? confirmRepair = null)
{
    // TODO(refactor): Experimental error-routing flag; replace with a supported workflow outcome contract.
    internal bool RepairWasStarted { get; private set; }

    // TODO(refactor): Experimental workflow continuation through repair; finalize the mutation boundary later.
    public async Task<WorkflowState> RunAsync(CommandLineOptions options, CancellationToken cancel,
        bool persistent = true, Action<string>? differenceOutput = null, Func<WorkflowState, Task>? comparisonReady = null)
    {
        using var workspace = new WorkflowWorkspace(options.WorkingDirectory
            ?? throw new InvalidOperationException("The execution workspace must be resolved before running the workflow."));
        var state = persistent ? workspace.Load() : null;
        if (state?.Phase == WorkflowPhase.RepairStarted)
        {
            RepairWasStarted = true;
            throw new InvalidDataException("Repair was already started. Run a new full check in a new workspace; this report cannot be reapplied.");
        }
        string? temporaryConnection = null;
        if (state == null)
        {
            if (options.ConnectionString == null || options.IndexPath == null)
                throw new InvalidDataException("A new check requires --connection-string and --index.");
            string connection;
            try { connection = new SqlConnectionStringBuilder(options.ConnectionString).ConnectionString; }
            catch (ArgumentException) { throw new InvalidDataException("Invalid SQL Server connection string."); }
            cancel.ThrowIfCancellationRequested();
            state = new WorkflowState
            {
                OriginalIndexPath = Path.GetFullPath(options.IndexPath),
                Index = operations.InspectIndex(options.IndexPath)
            };
            if (persistent)
                CheckpointWriter.AtomicWrite(workspace.FilePath(state.CredentialsFile), protector.Protect(connection, state.Id));
            else temporaryConnection = connection;
            Save(WorkflowPhase.Initialized);
        }
        else
        {
            if (options.IndexPath != null && !string.Equals(Path.GetFullPath(options.IndexPath), state.OriginalIndexPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("This working directory belongs to a different index. Use a new working directory for a new check.");
            if (options.ConnectionString != null)
            {
                var supplied = new SqlConnectionStringBuilder(options.ConnectionString).ConnectionString;
                if (!string.Equals(supplied, ReadConnection(), StringComparison.Ordinal))
                    throw new InvalidDataException("This working directory has different connection settings. Resume with -w only or use a new working directory.");
            }
            WritePhase("Resuming workflow: ", state.Phase, ConsoleColor.Yellow);
            if (state.Phase >= WorkflowPhase.DatabaseSaved)
                await CheckpointWriter.VerifyAsync(workspace.DirectoryPath, state.Database!.Data, cancel);
            if (state.Phase >= WorkflowPhase.IndexSaved)
            {
                await CheckpointWriter.VerifyAsync(workspace.DirectoryPath, state.IndexSnapshot!.Data, cancel);
                await CheckpointWriter.VerifyAsync(workspace.DirectoryPath, state.IndexSnapshot.Issues, cancel);
            }
            if (state.Phase >= WorkflowPhase.ComparisonSaved)
                await CheckpointWriter.VerifyAsync(workspace.DirectoryPath, state.Comparison!.Report, cancel);
            WorkflowSnapshotUpgrade.Run(workspace, state, diagnostics, cancel);
            if (state.Phase >= WorkflowPhase.ComparisonSaved)
                ComparisonSummaryUpgrade.Run(workspace, state, diagnostics, cancel);
        }

        if (state.Phase == WorkflowPhase.Initialized)
        {
            cancel.ThrowIfCancellationRequested();
            diagnostics.WriteLine("Saving database query results...");
            var connection = ReadConnection();
            try { state.Database = await operations.ExportDatabaseAsync(connection, workspace.FilePath(state.DatabaseFile), cancel); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { throw new IOException(Program.SanitizeError(error, connection)); }
            Save(WorkflowPhase.DatabaseSaved);
        }
        if (state.Phase == WorkflowPhase.DatabaseSaved)
        {
            cancel.ThrowIfCancellationRequested();
            diagnostics.WriteLine("Saving index query results and path comparison metadata...");
            state.IndexSnapshot = await operations.ExportIndexAsync(state.Index, workspace.FilePath(state.IndexFile),
                workspace.FilePath(state.IssuesFile), workspace.FilePath(state.ScratchFile), cancel);
            Save(WorkflowPhase.IndexSaved);
        }
        if (state.Phase == WorkflowPhase.IndexSaved)
        {
            cancel.ThrowIfCancellationRequested();
            diagnostics.WriteLine("Comparing the saved database and index snapshots...");
            state.Comparison = SavedComparison.Run(workspace, state, cancel, differenceOutput);
            Save(WorkflowPhase.ComparisonSaved);
        }
        if (state.Phase is WorkflowPhase.ComparisonSaved or WorkflowPhase.RepairPrepared)
        {
            cancel.ThrowIfCancellationRequested();
            if (comparisonReady != null) await comparisonReady(state);
            if (repairFactory == null)
            {
                if (state.Phase == WorkflowPhase.RepairPrepared) throw new InvalidOperationException("Repair continuation is unavailable.");
                Save(WorkflowPhase.Completed);
            }
            else
            {
                var connection = ReadConnection();
                try
                {
                    var hasActivity = state.Database!.LastActivityId.HasValue && state.IndexSnapshot!.Activity != null;
                    diagnostics.WriteLine("Preparing index repair...");
                    await using var repair = await repairFactory(connection, state.Index, cancel);
                    if (!hasActivity)
                    {
                        state.Database = state.Database with
                        { LastActivityId = state.Database.LastActivityId ?? await repair.ReadLastActivityIdAsync(cancel) };
                        state.IndexSnapshot = state.IndexSnapshot! with
                        { Activity = state.IndexSnapshot!.Activity ?? repair.OriginalIndexActivity };
                        if (persistent) workspace.Save(state);
                    }
                    if (await repair.PrepareAsync(workspace.FilePath(state.ReportFile), cancel))
                    {
                        state.Repair = repair.Summary;
                        Save(WorkflowPhase.RepairPrepared);
                        if (confirmRepair != null && await confirmRepair(state.Index, state.Repair, cancel))
                        {
                            cancel.ThrowIfCancellationRequested();
                            repair.ValidateBeforeStart();
                            // Set before Save as a conservative error-routing choice; no index write precedes the durable marker.
                            RepairWasStarted = true;
                            Save(WorkflowPhase.RepairStarted);
                            state.Repair = await repair.ApplyAsync(cancel);
                            state.Comparison = await SavedComparison.AppendRepairResultAsync(workspace, state, cancel);
                            Save(WorkflowPhase.RepairCompleted);
                        }
                        else diagnostics.WriteLine("Repair not started. The prepared workflow can be continued with -w in persistent mode.");
                    }
                    else Save(WorkflowPhase.Completed);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { throw new IOException(Program.SanitizeError(error, connection)); }
            }
        }
        if (persistent) diagnostics.WriteLine($"Report: {workspace.FilePath(state.ReportFile)}");
        return state;

        // TODO(refactor): Experimental checkpoint display; consolidate console presentation with the workflow API later.
        void Save(WorkflowPhase phase)
        {
            state!.Phase = phase;
            if (persistent) workspace.Save(state);
            if (persistent) WritePhase("Checkpoint: ", phase, ConsoleColor.Green);
            else diagnostics.WriteLine($"Stage completed: {phase}.");
            checkpointSaved?.Invoke(phase);
        }

        // TODO(refactor): Experimental status coloring; consolidate console presentation with the workflow API later.
        void WritePhase(string prefix, WorkflowPhase phase, ConsoleColor color)
        {
            diagnostics.Write(prefix);
            if (ReferenceEquals(diagnostics, Console.Error) && !Console.IsErrorRedirected)
            {
                var originalColor = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = color;
                    diagnostics.WriteLine($"{phase}.");
                }
                finally { Console.ForegroundColor = originalColor; }
            }
            else diagnostics.WriteLine($"{phase}.");
        }
        string ReadConnection() => persistent
            ? protector.Unprotect(File.ReadAllBytes(workspace.FilePath(state!.CredentialsFile)), state.Id)
            : temporaryConnection ?? throw new InvalidOperationException("Temporary connection settings are missing.");
    }
}
