namespace IndexIntegrityChecker;

public sealed record CheckCounts(long DatabaseVersions, long IndexDocuments, long MatchedDocuments);

public static class IntegrityChecker
{
    public static async Task<CheckCounts> CheckAsync(LuceneSnapshot index,
        IAsyncEnumerable<DatabaseVersion> database, DifferenceReporter reporter, CancellationToken cancel = default)
    {
        using var indexed = index.ReadVersions().GetEnumerator();
        await using var stored = database.GetAsyncEnumerator(cancel);
        var hasIndex = indexed.MoveNext();
        var hasDatabase = await stored.MoveNextAsync();
        long databaseCount = 0, indexCount = 0, matchedCount = 0;
        var previousDatabaseVersion = 0;
        while (hasIndex || hasDatabase)
        {
            cancel.ThrowIfCancellationRequested();
            if (hasDatabase && stored.Current.VersionId <= previousDatabaseVersion)
                throw new InvalidDataException("SQL versions must be unique, positive and ordered by VersionId.");
            if (hasDatabase && (!hasIndex || stored.Current.VersionId < indexed.Current.VersionId))
            {
                var row = stored.Current;
                reporter.Report(new(DifferenceKind.NotInIndex, row.VersionId, NodeId: row.NodeId));
                previousDatabaseVersion = row.VersionId;
                databaseCount++;
                hasDatabase = await stored.MoveNextAsync();
                continue;
            }

            var versionId = indexed.Current.VersionId;
            var matched = hasDatabase && stored.Current.VersionId == versionId;
            var groupCount = 0;
            do
            {
                cancel.ThrowIfCancellationRequested();
                var current = indexed.Current;
                var metadata = index.GetMetadata(current.DocId);
                if (++groupCount > 1)
                    reporter.Report(new(DifferenceKind.MoreDocument, versionId, current.DocId, metadata.NodeId));
                indexCount++;
                if (matched)
                {
                    matchedCount++;
                    Compare(stored.Current, current, metadata, index, reporter);
                }
                else reporter.Report(new(DifferenceKind.NotInDatabase, versionId, current.DocId, metadata.NodeId));
                hasIndex = indexed.MoveNext();
            } while (hasIndex && indexed.Current.VersionId == versionId);

            if (matched)
            {
                previousDatabaseVersion = stored.Current.VersionId;
                databaseCount++;
                hasDatabase = await stored.MoveNextAsync();
            }
        }
        return new(databaseCount, indexCount, matchedCount);
    }

    private static void Compare(DatabaseVersion database, IndexVersion document, IndexMetadata index,
        LuceneSnapshot snapshot, DifferenceReporter reporter)
    {
        void Different(DifferenceKind kind, string field, object? expected, object? actual) =>
            reporter.Report(new(kind, database.VersionId, document.DocId, database.NodeId, field, expected, actual));

        if ((index.Present & Fields.NodeId) != 0 && database.NodeId != index.NodeId)
            Different(DifferenceKind.DifferentNodeId, "Id", database.NodeId, index.NodeId);
        if ((index.Present & Fields.VersionTimestamp) != 0 && database.VersionTimestamp != index.VersionTimestamp)
            Different(DifferenceKind.DifferentVersionTimestamp, "VersionTimestamp", database.VersionTimestamp, index.VersionTimestamp);
        var isPublic = database.VersionId == database.LastMajorVersionId;
        var isDraft = database.VersionId == database.LastMinorVersionId;
        if ((index.Present & Fields.IsLastPublic) != 0 && isPublic != index.IsLastPublic)
            Different(DifferenceKind.DifferentLastPublicFlag, "IsLastPublic", isPublic, index.IsLastPublic);
        if ((index.Present & Fields.IsLastDraft) != 0 && isDraft != index.IsLastDraft)
            Different(DifferenceKind.DifferentLastDraftFlag, "IsLastDraft", isDraft, index.IsLastDraft);
        if ((index.Present & Fields.NodeTimestamp) != 0 && database.NodeTimestamp != index.NodeTimestamp &&
            !snapshot.HistoricalNodeTimestampIsAllowed(document.DocId))
            Different(DifferenceKind.DifferentNodeTimestamp, "NodeTimestamp", database.NodeTimestamp, index.NodeTimestamp);
    }
}
