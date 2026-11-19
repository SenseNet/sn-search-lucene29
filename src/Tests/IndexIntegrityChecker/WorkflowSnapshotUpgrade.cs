namespace IndexIntegrityChecker;

/// <summary>Converts verified binary or JSONL checkpoints without reopening either source.</summary>
internal static class WorkflowSnapshotUpgrade
{
    public static void Run(WorkflowWorkspace workspace, WorkflowState state, TextWriter diagnostics, CancellationToken cancel)
    {
        if (state.FormatVersion == WorkflowState.CurrentFormatVersion) return;
        var database = state.Database;
        var index = state.IndexSnapshot;
        var obsolete = new List<string>();
        if (state.Phase >= WorkflowPhase.DatabaseSaved)
        {
            diagnostics.WriteLine("Saving database snapshot as TSV...");
            var original = workspace.FilePath(state.DatabaseFile);
            using var output = new CheckpointWriter(Path.ChangeExtension(original, ".tsv"));
            var rows = state.FormatVersion == 1
                ? BinarySnapshotReader.ReadDatabase(original, database!.Data.Rows, cancel)
                : JsonlSnapshotReader.ReadDatabase(original, database!.Data.Rows, cancel);
            using (var writer = SnapshotFiles.StartDatabase(output.Stream))
                foreach (var row in rows)
                    SnapshotFiles.Write(writer, row);
            database = database! with { Data = output.Complete(database!.Data.Rows, cancel) };
            obsolete.Add(original);
        }
        if (state.Phase >= WorkflowPhase.IndexSaved)
        {
            diagnostics.WriteLine("Saving index snapshot as TSV...");
            var original = workspace.FilePath(state.IndexFile);
            using var output = new CheckpointWriter(Path.ChangeExtension(original, ".tsv"));
            var rows = state.FormatVersion == 1
                ? BinarySnapshotReader.ReadIndex(original, index!.Data.Rows, index.MaxDoc, cancel)
                : JsonlSnapshotReader.ReadIndex(original, index!.Data.Rows, index.MaxDoc, cancel);
            using (var writer = SnapshotFiles.StartIndex(output.Stream))
                foreach (var row in rows)
                    SnapshotFiles.Write(writer, row);
            index = index! with { Data = output.Complete(index!.Data.Rows, cancel) };
            obsolete.Add(original);
        }
        cancel.ThrowIfCancellationRequested();
        state.FormatVersion = WorkflowState.CurrentFormatVersion;
        state.Database = database;
        state.IndexSnapshot = index;
        workspace.Save(state);
        // The state references the complete TSV files before any input is removed.
        foreach (var path in obsolete)
        {
            try { File.Delete(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { diagnostics.WriteLine($"TSV checkpoint saved; could not remove unused file: {Path.GetFileName(path)}."); }
        }
    }
}
