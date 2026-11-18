using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace IndexIntegrityChecker;

public sealed class SqlVersionSource : IAsyncDisposable
{
    internal const string NodeTypesSql = "SELECT NodeTypeId, ParentId, Name FROM dbo.NodeTypes;";
    internal const string DefinitionsSql = """
        SELECT C.Name, F.[Stream], F.BlobProvider
        FROM dbo.Nodes AS C
        JOIN dbo.NodeTypes AS T ON T.NodeTypeId = C.NodeTypeId
        LEFT JOIN dbo.BinaryProperties AS B ON B.VersionId = C.LastMinorVersionId
            AND B.PropertyTypeId IN (SELECT PropertyTypeId FROM dbo.PropertyTypes WHERE Name = N'Binary')
        LEFT JOIN dbo.Files AS F ON F.FileId = B.FileId
        WHERE T.Name = N'ContentType' AND C.Path LIKE N'/Root/System/Schema/ContentTypes/%';
        """;
    internal const string VersionsSql = """
        SELECT V.VersionId, CONVERT(bigint, V.[Timestamp]) AS VersionTimestamp,
               N.NodeId, CONVERT(bigint, N.[Timestamp]) AS NodeTimestamp,
               N.LastMajorVersionId, N.LastMinorVersionId
        FROM dbo.Versions AS V
        INNER JOIN dbo.Nodes AS N ON N.NodeId = V.NodeId
        """;
    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;
    private readonly HashSet<int> _excluded;
    public int ExcludedTypeCount => _excluded.Count;
    public IsolationLevel Isolation => _transaction.IsolationLevel;

    private SqlVersionSource(SqlConnection connection, SqlTransaction transaction, HashSet<int> excluded)
    {
        _connection = connection;
        _transaction = transaction;
        _excluded = excluded;
    }

    public static async Task<SqlVersionSource> OpenAsync(string connectionString, CancellationToken cancel)
    {
        // Validate without ever echoing connection-string values in an error message.
        try { _ = new SqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException) { throw new InvalidDataException("Invalid SQL Server connection string."); }
        var connection = new SqlConnection(connectionString);
        SqlTransaction? transaction = null;
        try
        {
            await connection.OpenAsync(cancel);
            using var isolationCommand = connection.CreateCommand();
            isolationCommand.CommandText = "SELECT snapshot_isolation_state FROM sys.databases WHERE database_id = DB_ID();";
            var snapshot = Convert.ToInt32(await isolationCommand.ExecuteScalarAsync(cancel), CultureInfo.InvariantCulture) == 1;
            transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                snapshot ? IsolationLevel.Snapshot : IsolationLevel.Serializable, cancel);
            var excluded = await ReadExcludedTypesAsync(connection, transaction, cancel);
            return new(connection, transaction, excluded);
        }
        catch
        {
            if (transaction != null) await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<HashSet<int>> ReadExcludedTypesAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken cancel)
    {
        var types = new List<NodeTypeDefinition>();
        using (var command = new SqlCommand(NodeTypesSql, connection, transaction) { CommandTimeout = 0 })
        await using (var reader = await command.ExecuteReaderAsync(cancel))
            while (await reader.ReadAsync(cancel))
                types.Add(new(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), reader.GetString(2)));

        var definitions = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
        using (var command = new SqlCommand(DefinitionsSql, connection, transaction) { CommandTimeout = 0 })
        await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancel))
        {
            while (await reader.ReadAsync(cancel))
            {
                var name = reader.GetString(0);
                if (reader.IsDBNull(1))
                    throw new InvalidDataException($"Cannot read the content type definition '{name}' from SQL. " +
                        "Its Binary stream is missing or stored in an external blob provider; indexing exclusions cannot be verified.");
                using var xml = reader.GetStream(1);
                if (!definitions.TryAdd(name, ContentTypePolicy.ReadAllowIndexing(xml)))
                    throw new InvalidDataException($"Multiple definitions found for content type '{name}'.");
            }
        }
        return ContentTypePolicy.GetExcluded(types, definitions);
    }

    internal static string BuildVersionsSql(IEnumerable<int> excluded)
    {
        // IDs originate from typed database rows, not command-line text. No parameter count limit.
        var ids = excluded.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
        return VersionsSql + (ids.Length == 0 ? "\n" : $"\nWHERE N.NodeTypeId NOT IN ({string.Join(',', ids)})\n") + "ORDER BY V.VersionId;";
    }

    public async IAsyncEnumerable<DatabaseVersion> ReadVersionsAsync([EnumeratorCancellation] CancellationToken cancel = default)
    {
        using var command = new SqlCommand(BuildVersionsSql(_excluded), _connection, _transaction) { CommandTimeout = 0 };
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancel);
        while (await reader.ReadAsync(cancel))
            yield return new(reader.GetInt32(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt64(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetInt32(5));
    }

    // TODO(refactor): Experimental reuse of the read-only MSSQL activity query; consolidate offline source APIs later.
    internal Task<int> GetLastIndexingActivityIdAsync(CancellationToken cancel) =>
        MsSqlRepairSource.GetLastIndexingActivityIdAsync(_connection, _transaction, cancel);

    public Task CompleteAsync(CancellationToken cancel) => _transaction.CommitAsync(cancel);
    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
