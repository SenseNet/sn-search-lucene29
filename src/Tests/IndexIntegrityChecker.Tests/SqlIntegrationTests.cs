using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IndexIntegrityChecker.Tests;

[TestClass]
public class SqlIntegrationTests
{
    // TODO(refactor): Experimental MSSQL repair projection test; retain as acceptance coverage for the offline API.
    [TestMethod]
    [TestCategory("SqlIntegration")]
    public async Task RepairSelectReadsByteFlagsRowversionsAndLastIdentityAfterCleanup()
    {
        var connectionString = Environment.GetEnvironmentVariable("INDEX_CHECKER_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Inconclusive("Set INDEX_CHECKER_TEST_SQL_CONNECTION. This fixture creates only connection-local temporary tables.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var setup = new SqlCommand("""
            CREATE TABLE #RepairNodes (NodeId int, NodeTypeId int, ParentNodeId int NULL, Path nvarchar(200),
              IsSystem tinyint, Timestamp rowversion, LastMajorVersionId int NULL, LastMinorVersionId int NULL);
            CREATE TABLE #RepairVersions (VersionId int, NodeId int, Timestamp rowversion, IndexDocument nvarchar(max), Status smallint);
            CREATE TABLE #RepairActivities (IndexingActivityId int IDENTITY(1,1), Payload int);
            INSERT #RepairNodes (NodeId,NodeTypeId,ParentNodeId,Path,IsSystem,LastMajorVersionId,LastMinorVersionId)
              VALUES (1,5,NULL,N'/Root/A',1,10,10);
            INSERT #RepairVersions (VersionId,NodeId,IndexDocument,Status) VALUES (10,1,N'[]',1);
            INSERT #RepairActivities (Payload) VALUES (1),(2),(3);
            DELETE FROM #RepairActivities WHERE IndexingActivityId >= 2;
            """, connection);
        await setup.ExecuteNonQueryAsync();
        var sql = MsSqlRepairSource.VersionSql.Replace("dbo.Nodes", "#RepairNodes").Replace("dbo.Versions", "#RepairVersions");
        using var select = new SqlCommand(sql, connection);
        select.Parameters.Add("@version", SqlDbType.Int).Value = 10;
        RepairVersion first;
        await using (var reader = await select.ExecuteReaderAsync())
        {
            Assert.IsTrue(await reader.ReadAsync());
            first = MsSqlRepairSource.ReadVersionRow(reader, []);
        }
        Assert.IsTrue(first.IsSystem);
        Assert.AreEqual(0, first.ParentId);
        Assert.AreEqual(1, first.Status);
        Assert.IsTrue(first.NodeTimestamp > 0);
        Assert.IsTrue(first.VersionTimestamp > 0);
        await using (var reader = await select.ExecuteReaderAsync())
        {
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(first, MsSqlRepairSource.ReadVersionRow(reader, []));
            Assert.IsTrue(MsSqlRepairSource.ReadVersionRow(reader, [5]).Excluded);
        }
        // Use the production identity projection against the isolated fixture's metadata.
        var identitySql = MsSqlRepairSource.LastActivitySql.Replace("sys.identity_columns", "tempdb.sys.identity_columns")
            .Replace("sys.tables", "tempdb.sys.tables")
            .Replace("t.name=N'IndexingActivities' AND SCHEMA_NAME(t.schema_id)=N'dbo'", "t.object_id=OBJECT_ID('tempdb..#RepairActivities')");
        using var activity = new SqlCommand(identitySql, connection);
        Assert.AreEqual(3, Convert.ToInt32(await activity.ExecuteScalarAsync()));
    }

    [TestMethod]
    [TestCategory("SqlIntegration")]
    public async Task RealSqlQueriesExcludeDisabledTypesAndProduceOrderedMergeInput()
    {
        var connectionString = Environment.GetEnvironmentVariable("INDEX_CHECKER_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Inconclusive("Set INDEX_CHECKER_TEST_SQL_CONNECTION to run this test. Only connection-local temporary tables are created.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using (var setup = connection.CreateCommand())
        {
            setup.CommandText = """
                CREATE TABLE #NodeTypes (NodeTypeId int, ParentId int NULL, Name nvarchar(100));
                CREATE TABLE #Nodes (NodeId int, NodeTypeId int, Name nvarchar(100), Path nvarchar(450),
                    LastMajorVersionId int NULL, LastMinorVersionId int NULL, [Timestamp] rowversion);
                CREATE TABLE #Versions (VersionId int, NodeId int, [Timestamp] rowversion);
                CREATE TABLE #PropertyTypes (PropertyTypeId int, Name nvarchar(100));
                CREATE TABLE #BinaryProperties (VersionId int, PropertyTypeId int, FileId int);
                CREATE TABLE #Files (FileId int, [Stream] varbinary(max), BlobProvider nvarchar(450));
                INSERT #NodeTypes VALUES (1,NULL,'Root'),(2,1,'Disabled'),(3,1,'ContentType'),(4,2,'Inherited');
                INSERT #Nodes (NodeId,NodeTypeId,Name,Path,LastMajorVersionId,LastMinorVersionId) VALUES
                    (1,1,'Enabled','/Root/Enabled',NULL,10), (2,2,'Excluded','/Root/Excluded',20,20),
                    (3,3,'Disabled','/Root/System/Schema/ContentTypes/Disabled',30,30),
                    (4,4,'ExcludedChild','/Root/ExcludedChild',40,40);
                INSERT #Versions (VersionId,NodeId) VALUES (40,4),(30,3),(20,2),(10,1);
                INSERT #PropertyTypes VALUES (1,'Binary');
                INSERT #BinaryProperties VALUES (30,1,1);
                """;
            await setup.ExecuteNonQueryAsync();
        }
        // Create #tables in session scope, before the parameterized sp_executesql batch.
        using (var insert = new SqlCommand("INSERT #Files VALUES (1,@xml,NULL);", connection))
        {
            insert.Parameters.Add("@xml", SqlDbType.VarBinary, -1).Value = Encoding.UTF8.GetBytes(
                "<ContentType name='Disabled'><AllowIndexing>false</AllowIndexing></ContentType>");
            await insert.ExecuteNonQueryAsync();
        }

        var types = new List<NodeTypeDefinition>();
        using (var command = new SqlCommand(Temp(SqlVersionSource.NodeTypesSql), connection))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                types.Add(new(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), reader.GetString(2)));
        var definitions = new Dictionary<string, bool?>();
        using (var command = new SqlCommand(Temp(SqlVersionSource.DefinitionsSql), connection))
        await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
        {
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                using var stream = reader.GetStream(1);
                definitions.Add(name, ContentTypePolicy.ReadAllowIndexing(stream));
            }
        }
        var excluded = ContentTypePolicy.GetExcluded(types, definitions);
        CollectionAssert.AreEquivalent(new[] { 2, 4 }, excluded.ToArray());

        var rows = new List<DatabaseVersion>();
        using (var command = new SqlCommand(Temp(SqlVersionSource.BuildVersionsSql(excluded)), connection))
        await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
            while (await reader.ReadAsync())
                rows.Add(new(reader.GetInt32(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt64(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetInt32(5)));
        CollectionAssert.AreEqual(new[] { 10, 30 }, rows.Select(x => x.VersionId).ToArray());
        Assert.IsNull(rows[0].LastMajorVersionId);
        var documents = rows.Select(row => CheckerTests.Doc(row.VersionId, row.NodeId, row.NodeTimestamp,
            row.VersionTimestamp, row.VersionId == row.LastMajorVersionId, row.VersionId == row.LastMinorVersionId)).ToArray();
        var result = await CheckerTests.Check(documents, rows.ToArray());
        Assert.AreEqual(0, result.Diffs.Count);
        Assert.AreEqual(new CheckCounts(2, 2, 2), result.Counts);
    }

    private static string Temp(string sql)
    {
        foreach (var name in new[] { "NodeTypes", "Nodes", "Versions", "PropertyTypes", "BinaryProperties", "Files" })
            sql = sql.Replace("dbo." + name, "#" + name, StringComparison.Ordinal);
        return sql;
    }
}
