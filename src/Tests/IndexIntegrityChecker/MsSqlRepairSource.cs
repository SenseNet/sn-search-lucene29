using System.Data;
using Microsoft.Data.SqlClient;

namespace IndexIntegrityChecker;

// TODO(refactor): Experimental MSSQL-only source; integrate into a supported offline API after validation.
internal sealed class MsSqlRepairSource : IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;
    private HashSet<int> _excluded = [];

    // TODO(refactor): Experimental schema contract; consolidate with an offline schema API.
    internal RepairSchema Schema { get; } = new();

    // TODO(refactor): Experimental source lifetime; refactor transaction ownership after the experiment.
    private MsSqlRepairSource(SqlConnection connection, SqlTransaction transaction)
    { _connection = connection; _transaction = transaction; }

    // TODO(refactor): Experimental MSSQL initialization; share read-only connection setup after validation.
    internal static async Task<MsSqlRepairSource> OpenAsync(string connectionString, CancellationToken cancel)
    {
        var connection = new SqlConnection(connectionString);
        MsSqlRepairSource? source = null;
        try
        {
            await connection.OpenAsync(cancel);
            using var isolation = new SqlCommand("SELECT snapshot_isolation_state FROM sys.databases WHERE database_id = DB_ID();", connection);
            var snapshot = Convert.ToInt32(await isolation.ExecuteScalarAsync(cancel)) == 1;
            var transaction = (SqlTransaction)await connection.BeginTransactionAsync(snapshot ? IsolationLevel.Snapshot : IsolationLevel.Serializable, cancel);
            source = new(connection, transaction);
            await source.LoadSchemaAsync(cancel);
            return source;
        }
        catch
        {
            if (source != null) await source.DisposeAsync(); else await connection.DisposeAsync();
            throw;
        }
    }

    // TODO(refactor): Experimental reuse of checker CTD reads; replace with a supported offline schema loader.
    private async Task LoadSchemaAsync(CancellationToken cancel)
    {
        var types = new List<NodeTypeDefinition>();
        using (var command = Command(SqlVersionSource.NodeTypesSql))
        await using (var reader = await command.ExecuteReaderAsync(cancel))
            while (await reader.ReadAsync(cancel)) types.Add(new(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), reader.GetString(2)));
        var exclusions = new Dictionary<string, bool?>();
        var definitionsByType = new Dictionary<string, string>();
        using (var command = Command(SqlVersionSource.DefinitionsSql))
        await using (var reader = await command.ExecuteReaderAsync(cancel))
            while (await reader.ReadAsync(cancel))
            {
                var name = reader.GetString(0);
                if (reader.IsDBNull(1)) throw new InvalidDataException($"Content type '{name}' is unavailable in SQL (missing/external Binary).");
                using var stream = reader.GetStream(1);
                using var text = new StreamReader(stream);
                var xml = await text.ReadToEndAsync(cancel);
                using var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
                if (!exclusions.TryAdd(name, ContentTypePolicy.ReadAllowIndexing(bytes))) throw new InvalidDataException($"Duplicate content type '{name}'.");
                definitionsByType.Add(name, xml);
            }
        _excluded = ContentTypePolicy.GetExcluded(types, exclusions);
        var typesById = types.ToDictionary(t => t.Id);
        var loaded = new HashSet<int>();
        // TODO(refactor): Experimental base-first ordering; share the canonical inheritance resolver later.
        void AddType(int id)
        {
            if (!loaded.Add(id)) return;
            var type = typesById[id];
            if (type.ParentId is > 0) AddType(type.ParentId.Value);
            if (definitionsByType.TryGetValue(type.Name, out var xml)) Schema.AddDefinition(xml);
        }
        foreach (var type in types) AddType(type.Id);
        // Definitions use the last draft, as do the repository's CTD/aspect loaders.
        using var dynamic = Command(DynamicDefinitionsSql);
        await using var definitions = await dynamic.ExecuteReaderAsync(cancel);
        while (await definitions.ReadAsync(cancel))
        {
            if (definitions.IsDBNull(2)) throw new InvalidDataException("A dynamic field definition is unavailable in SQL.");
            Schema.AddDefinition(definitions.GetString(2), definitions.GetString(1) == "AspectDefinition" ? definitions.GetString(0) + "." : "");
        }
    }

    // TODO(refactor): Experimental SELECT adapted from sensenet@91be9c5c3f7d5f5aa03c9a72cde4a935f106584f
    // src/ContentRepository.MsSql/MsSqlDataProviderScripts.cs::LoadNodesScript (LongTextProperties)
    // and src/ContentRepository/Aspect.cs::GetPerFieldIndexingInfo. Share schema queries later.
    internal const string DynamicDefinitionsSql = """
        SELECT N.Name, P.Name, L.Value FROM dbo.Nodes N
        JOIN dbo.LongTextProperties L ON L.VersionId = N.LastMinorVersionId
        JOIN dbo.PropertyTypes P ON P.PropertyTypeId = L.PropertyTypeId
        WHERE P.Name IN (N'AspectDefinition', N'ContentListDefinition');
        """;

    // TODO(refactor): Experimental SELECT adapted from sensenet@91be9c5c3f7d5f5aa03c9a72cde4a935f106584f
    // src/ContentRepository.MsSql/MsSqlDataProviderScripts.cs::LoadIndexDocumentsCommonScript.
    // Adaptation: one parameterized version and timestamps as bigint; share offline reads later.
    internal const string VersionSql = """
        SELECT V.VersionId, N.NodeId, N.NodeTypeId, N.ParentNodeId, N.Path, N.IsSystem,
          CONVERT(bigint,N.Timestamp), CONVERT(bigint,V.Timestamp), N.LastMajorVersionId,
          N.LastMinorVersionId, V.IndexDocument, V.Status
        FROM dbo.Nodes N JOIN dbo.Versions V ON V.NodeId=N.NodeId WHERE V.VersionId=@version;
        """;

    // TODO(refactor): Experimental MSSQL row reader; extract supported version-level offline reads later.
    internal async Task<RepairVersion?> ReadVersionAsync(int version, CancellationToken cancel)
    {
        using var command = Command(VersionSql);
        command.Parameters.Add("@version", SqlDbType.Int).Value = version;
        await using var reader = await command.ExecuteReaderAsync(cancel);
        if (!await reader.ReadAsync(cancel)) return null;
        return ReadVersionRow(reader, _excluded);
    }

    // TODO(refactor): Experimental adaptation of sensenet@91be9c5c3f7d5f5aa03c9a72cde4a935f106584f
    // src/Storage/Data/RelationalDataProviderBase.cs::GetIndexDocumentDataFromReader.
    // Adaptation: explicit projection and no repository objects; publish a supported offline row mapper later.
    internal static RepairVersion ReadVersionRow(SqlDataReader reader, HashSet<int> excluded)
    {
        return new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.IsDBNull(3) ? 0 : reader.GetInt32(3), reader.GetString(4),
            !reader.IsDBNull(5) && Convert.ToBoolean(reader.GetValue(5)), reader.GetInt64(6), reader.GetInt64(7), reader.IsDBNull(8) ? null : reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetInt32(9), reader.IsDBNull(10) ? null : reader.GetString(10), excluded.Contains(reader.GetInt32(2)), Convert.ToInt32(reader.GetValue(11)));
    }

    // TODO(refactor): Experimental copy from sensenet@91be9c5c3f7d5f5aa03c9a72cde4a935f106584f
    // src/ContentRepository.MsSql/MsSqlDataProviderScripts.cs::GetLastIndexingActivityIdScript.
    // Adaptation: schema-qualified identity lookup; publish a supported offline query after the experiment.
    internal const string LastActivitySql = """
        SELECT CASE WHEN i.last_value IS NULL THEN 0 ELSE CONVERT(int,i.last_value) END
        FROM sys.identity_columns i JOIN sys.tables t ON i.object_id=t.object_id
        WHERE t.name=N'IndexingActivities' AND SCHEMA_NAME(t.schema_id)=N'dbo';
        """;

    // TODO(refactor): Experimental adaptation of sensenet@91be9c5c3f7d5f5aa03c9a72cde4a935f106584f
    // src/Storage/Data/RelationalDataProviderBase.cs::GetLastIndexingActivityIdAsync.
    // Adaptation: direct SqlCommand; reject invisible/missing identity metadata instead of silently using zero.
    internal Task<int> GetLastIndexingActivityIdAsync(CancellationToken cancel) =>
        GetLastIndexingActivityIdAsync(_connection, _transaction, cancel);

    // TODO(refactor): Experimental shared SELECT for checking and repair; expose a supported offline activity reader later.
    internal static async Task<int> GetLastIndexingActivityIdAsync(SqlConnection connection,
        SqlTransaction? transaction, CancellationToken cancel)
    {
        using var command = new SqlCommand(LastActivitySql, connection, transaction) { CommandTimeout = 0 };
        var result = await command.ExecuteScalarAsync(cancel);
        if (result is null or DBNull) throw new InvalidDataException("IndexingActivities identity metadata is unavailable.");
        return Convert.ToInt32(result);
    }

    // TODO(refactor): Experimental read command factory; consolidate MSSQL resource handling later.
    private SqlCommand Command(string sql) => new(sql, _connection, _transaction) { CommandTimeout = 0 };

    // TODO(refactor): Experimental source disposal; integrate into a supported offline session later.
    public async ValueTask DisposeAsync()
    {
        try { await _transaction.DisposeAsync(); }
        finally { await _connection.DisposeAsync(); }
    }
}
