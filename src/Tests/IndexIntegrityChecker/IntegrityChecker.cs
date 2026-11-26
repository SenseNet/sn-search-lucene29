namespace IndexIntegrityChecker;

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
        var reportedDraftPaths = new HashSet<int>();
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
                    IndexIntegrityComparer.CompareVersion(stored.Current, new(current.VersionId, current.DocId, metadata),
                        reporter, reportedDraftPaths.Add);
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
}
