using System.Text.Json;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Directory = System.IO.Directory;

namespace IndexIntegrityChecker.Tests;

// TODO(refactor): Experimental repair integration tests; retain as acceptance coverage for a supported API.
[TestClass]
public class RepairTests
{
    // TODO(refactor): Experimental real-index fixture; consolidate with existing checker fixtures later.
    internal sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "IndexRepairTests", Guid.NewGuid().ToString("N"));
        internal readonly Dictionary<int, RepairVersion> Rows = [];
        internal int Activity = 99;
        // TODO(refactor): Experimental index fixture path; consolidate test lifecycle later.
        internal string IndexPath => Path.Combine(Root, "index");
        // TODO(refactor): Experimental report fixture path; consolidate test lifecycle later.
        internal string ReportPath => Path.Combine(Root, "comparison-test.jsonl");
        // TODO(refactor): Experimental identity capture; use a common fixture after the API stabilizes.
        internal IndexIdentity Identity => new WorkflowOperations(TextWriter.Null).InspectIndex(IndexPath);

        // TODO(refactor): Experimental fixture setup; consolidate real Lucene fixtures after validation.
        internal Fixture(IEnumerable<Document> documents, IEnumerable<RepairVersion> rows, IDictionary<string, string>? data = null)
        {
            Directory.CreateDirectory(IndexPath);
            foreach (var row in rows) Rows.Add(row.VersionId, row);
            using var directory = FSDirectory.Open(new DirectoryInfo(IndexPath));
            using var analyzer = new KeywordAnalyzer();
            var writer = new IndexWriter(directory, analyzer, true, IndexWriter.MaxFieldLength.UNLIMITED);
            foreach (var document in documents) writer.AddDocument(document);
            LuceneRepairWriter.ForceCommit(writer, data ?? new Dictionary<string, string> { ["LastActivityId"] = "2", ["Gaps"] = "1", ["other"] = "keep" });
            writer.Close();
        }

        // TODO(refactor): Experimental schema fixture; replace with canonical schema parity fixtures later.
        internal static RepairSchema Schema()
        {
            var schema = new RepairSchema();
            schema.AddDefinition("""
                <ContentType name="Test"><Fields>
                <Field name="Name" type="ShortText"><Indexing><Store>Yes</Store><Mode>NotAnalyzed</Mode></Indexing></Field>
                <Field name="Path" type="ShortText"><Indexing><Store>Yes</Store><Mode>NotAnalyzed</Mode></Indexing></Field>
                <Field name="InTree" type="ShortText"><Indexing><Mode>NotAnalyzed</Mode></Indexing></Field>
                <Field name="InFolder" type="ShortText"><Indexing><Mode>NotAnalyzed</Mode></Indexing></Field>
                <Field name="Description" type="LongText" />
                </Fields></ContentType>
                """);
            return schema;
        }

        // TODO(refactor): Experimental report fixture; consolidate with normal checker workflow tests later.
        internal async Task<List<Difference>> CompareAsync(bool save = true)
        {
            using var directory = FSDirectory.Open(new DirectoryInfo(IndexPath));
            using var reader = IndexReader.Open(directory, true);
            var differences = new List<Difference>();
            var reporter = new DifferenceReporter(differences.Add);
            using var snapshot = new LuceneSnapshot(reader, reporter);
            var rows = Rows.Values.Where(r => !r.Excluded).OrderBy(r => r.VersionId)
                .Select(r => new DatabaseVersion(r.VersionId, r.VersionTimestamp, r.NodeId, r.NodeTimestamp, r.LastPublic, r.LastDraft));
            await IntegrityChecker.CheckAsync(snapshot, CheckerTests.Rows(rows), reporter);
            if (save)
            {
                var lines = differences.Select(d => JsonSerializer.Serialize(new
                { type = "difference", d.Kind, d.VersionId, d.DocId, d.NodeId, d.Field, d.Expected, d.Actual }, JsonOutput.Options)).ToList();
                lines.Add(JsonSerializer.Serialize(new { type = "summary", differences = reporter.Count, countsByKind = reporter.Counts }, JsonOutput.Options));
                File.WriteAllLines(ReportPath, lines);
            }
            return differences;
        }

        // TODO(refactor): Experimental delegate seam; retain only the minimum fixture hook in the final API.
        internal ComparisonRepairer Repair(Action<string>? progress = null, IndexIdentity? identity = null) =>
            new(identity ?? Identity, Schema(), (id, _) => Task.FromResult(Rows.GetValueOrDefault(id)), _ => Task.FromResult(Activity), progress: progress);

        // TODO(refactor): Experimental commit assertion; consolidate index inspection fixtures later.
        internal Dictionary<string, string> Data()
        {
            using var directory = FSDirectory.Open(new DirectoryInfo(IndexPath));
            using var reader = IndexReader.Open(directory, true);
            return new(reader.GetCommitUserData());
        }

        // TODO(refactor): Experimental fixture disposal; consolidate temporary test directories later.
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    // TODO(refactor): Experimental serialized document fixture; retain canonical format examples for the shared API.
    internal static RepairVersion Row(int version = 10, int node = 1, int? last = null) =>
        new(version, node, 1, 0, "/root/a", false, 100, 200, last ?? version, last ?? version,
            JsonSerializer.Serialize(new object[]
            {
                new { Name = "Id", Type = "Int", Store = "Yes", Value = node },
                new { Name = "VersionId", Type = "Int", Store = "Yes", Value = version },
                new { Name = "Description", Type = "String", Value = "The Quick Brown Fox" }
            }));

    // TODO(refactor): Experimental coverage of all checker differences; retain as repair acceptance tests.
    [DataTestMethod]
    [DataRow("NotInIndex")]
    [DataRow("NotInDatabase")]
    [DataRow("MoreDocument")]
    [DataRow("DifferentNodeId")]
    [DataRow("DifferentNodeTimestamp")]
    [DataRow("DifferentVersionTimestamp")]
    [DataRow("DifferentLastPublicFlag")]
    [DataRow("DifferentLastDraftFlag")]
    [DataRow("MissingField")]
    [DataRow("InvalidField")]
    [DataRow("MultipleFieldValues")]
    public async Task EachDifferenceRepairsToFreshConsistency(string name)
    {
        var kind = Enum.Parse<DifferenceKind>(name);
        var doc = CheckerTests.Doc();
        var documents = new List<Document> { doc };
        var rows = new List<RepairVersion> { Row() };
        switch (kind)
        {
            case DifferenceKind.NotInIndex: documents.Clear(); break;
            case DifferenceKind.NotInDatabase: rows.Clear(); break;
            case DifferenceKind.MoreDocument: documents.Add(CheckerTests.Doc()); break;
            case DifferenceKind.DifferentNodeId: documents[0] = CheckerTests.Doc(node: 2); break;
            case DifferenceKind.DifferentNodeTimestamp: documents[0] = CheckerTests.Doc(nodeTs: 101); break;
            case DifferenceKind.DifferentVersionTimestamp: documents[0] = CheckerTests.Doc(versionTs: 201); break;
            case DifferenceKind.DifferentLastPublicFlag: documents[0] = CheckerTests.Doc(isPublic: false); break;
            case DifferenceKind.DifferentLastDraftFlag: documents[0] = CheckerTests.Doc(isDraft: false); break;
            case DifferenceKind.MissingField: doc.RemoveField("NodeTimestamp"); break;
            case DifferenceKind.InvalidField: doc.RemoveField("IsLastDraft"); doc.Add(new Field("IsLastDraft", "broken", Field.Store.NO, Field.Index.NOT_ANALYZED)); break;
            case DifferenceKind.MultipleFieldValues:
                doc.Add(new NumericField("VersionId", Field.Store.NO, true).SetIntValue(20)); rows.Add(Row(20, 2)); break;
        }
        using var fixture = new Fixture(documents, rows);
        Assert.IsTrue((await fixture.CompareAsync()).Any(d => d.Kind == kind));
        await using (var repair = fixture.Repair())
        {
            Assert.IsTrue(await repair.PrepareAsync(fixture.ReportPath, default));
            await repair.ApplyAsync(default);
        }
        Assert.AreEqual(0, (await fixture.CompareAsync(false)).Count);
        Assert.AreEqual("99", fixture.Data()["LastActivityId"]);
        Assert.IsFalse(fixture.Data().ContainsKey("Gaps"));
        Assert.AreEqual("keep", fixture.Data()["other"]);
    }

    // TODO(refactor): Experimental malformed-document closure test; retain for the supported repair implementation.
    [TestMethod]
    public async Task MultiVersionClosureRestoresEveryVersionAndRemovesDuplicates()
    {
        var a = CheckerTests.Doc(); a.Add(new NumericField("VersionId", Field.Store.NO, true).SetIntValue(20));
        var b = CheckerTests.Doc(20, 2); b.Add(new NumericField("VersionId", Field.Store.NO, true).SetIntValue(30));
        var unknown = CheckerTests.Doc(40, 4); unknown.RemoveField("VersionId");
        using var fixture = new Fixture([a, b, CheckerTests.Doc(30, 3), unknown], [Row(), Row(20, 2), Row(30, 3), Row(40, 4)]);
        await fixture.CompareAsync();
        await using (var repair = fixture.Repair())
        {
            await repair.PrepareAsync(fixture.ReportPath, default);
            Assert.AreEqual(4, repair.Summary.DeletedDocuments);
            Assert.AreEqual(4, repair.Summary.WrittenVersions);
            await repair.ApplyAsync(default);
        }
        Assert.AreEqual(0, (await fixture.CompareAsync(false)).Count);
    }

    // TODO(refactor): Experimental per-version isolation test; retain as a supported API guarantee.
    [TestMethod]
    public async Task OldVersionRepairLeavesLatestVersionUntouched()
    {
        var latest = CheckerTests.Doc(11); latest.Add(new Field("Sentinel", "unchanged", Field.Store.YES, Field.Index.NOT_ANALYZED));
        using var fixture = new Fixture([CheckerTests.Doc(10, versionTs: 5, isPublic: false, isDraft: false), latest], [Row(last: 11), Row(11)]);
        await fixture.CompareAsync();
        await using (var repair = fixture.Repair())
        {
            await repair.PrepareAsync(fixture.ReportPath, default);
            Assert.AreEqual(1, repair.Summary.WrittenVersions);
            await repair.ApplyAsync(default);
        }
        Assert.AreEqual(0, (await fixture.CompareAsync(false)).Count);
        using var directory = FSDirectory.Open(new DirectoryInfo(fixture.IndexPath));
        using var reader = IndexReader.Open(directory, true);
        var terms = reader.TermDocs(new Term("VersionId", NumericUtils.IntToPrefixCoded(11)));
        try { Assert.IsTrue(terms.Next()); Assert.AreEqual("unchanged", reader.Document(terms.Doc()).Get("Sentinel")); }
        finally { terms.Close(); }
    }

    // TODO(refactor): Experimental metadata-only commit test; retain against the actual Lucene implementation.
    [TestMethod]
    public async Task MetadataOnlyRepairForcesDurableCommitAndThenBecomesNoOp()
    {
        using var fixture = new Fixture([CheckerTests.Doc()], [Row()]);
        var original = fixture.Identity;
        Assert.AreEqual(0, (await fixture.CompareAsync()).Count);
        await using (var repair = fixture.Repair())
        {
            Assert.IsTrue(await repair.PrepareAsync(fixture.ReportPath, default));
            Assert.AreEqual(0, repair.Summary.WrittenVersions);
            Assert.AreEqual(0, repair.Summary.DeletedDocuments);
            await repair.ApplyAsync(default);
        }
        Assert.AreNotEqual(original.CommitVersion, fixture.Identity.CommitVersion);
        Assert.AreEqual("99", fixture.Data()["LastActivityId"]);
        await fixture.CompareAsync();
        var after = fixture.Identity;
        await using (var repair = fixture.Repair()) Assert.IsFalse(await repair.PrepareAsync(fixture.ReportPath, default));
        Assert.AreEqual(after, fixture.Identity);
    }

    // TODO(refactor): Experimental failure containment tests; retain as offline repair acceptance criteria.
    [DataTestMethod]
    [DataRow("deletions_committed")]
    [DataRow("version_written")]
    public async Task InterruptedRepairDoesNotPublishSuccessActivity(string stop)
    {
        using var fixture = new Fixture([CheckerTests.Doc(versionTs: 5)], [Row()]);
        await fixture.CompareAsync();
        await using (var repair = fixture.Repair(stage => { if (stage == stop) throw new OperationCanceledException(); }))
        {
            await repair.PrepareAsync(fixture.ReportPath, default);
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => repair.ApplyAsync(default));
        }
        Assert.AreEqual("2", fixture.Data()["LastActivityId"]);
        Assert.AreEqual("1", fixture.Data()["Gaps"]);
    }

    // TODO(refactor): Experimental preflight rejection test; retain SQL corruption protection in the final API.
    [DataTestMethod]
    [DataRow("missing")]
    [DataRow("invalid")]
    [DataRow("wrong-id")]
    public async Task InvalidSqlDocumentPreventsEveryIndexWrite(string fault)
    {
        var row = Row() with { SerializedDocument = fault switch { "missing" => null, "invalid" => "broken-json", _ => Row(node: 2).SerializedDocument } };
        using var fixture = new Fixture([CheckerTests.Doc(versionTs: 5)], [row]);
        await fixture.CompareAsync();
        var before = fixture.Identity;
        await using var repair = fixture.Repair();
        try { await repair.PrepareAsync(fixture.ReportPath, default); Assert.Fail("Expected preflight failure."); }
        catch (Exception e) when (e is InvalidDataException or JsonException) { }
        Assert.AreEqual(before, fixture.Identity);
    }

    // TODO(refactor): Experimental excluded-content behavior; retain alongside normal indexing exclusions.
    [TestMethod]
    public async Task ExcludedVersionIsOnlyDeleted()
    {
        using var fixture = new Fixture([CheckerTests.Doc()], [Row() with { Excluded = true, SerializedDocument = null }]);
        await fixture.CompareAsync();
        await using (var repair = fixture.Repair())
        {
            await repair.PrepareAsync(fixture.ReportPath, default);
            Assert.AreEqual(0, repair.Summary.WrittenVersions);
            await repair.ApplyAsync(default);
        }
        Assert.AreEqual(0, (await fixture.CompareAsync(false)).Count);
    }

    // TODO(refactor): Experimental changing-source protection; integrate with supported mutation guards.
    [TestMethod]
    public async Task ChangedActivityDoesNotClearGaps()
    {
        using var fixture = new Fixture([CheckerTests.Doc(versionTs: 5)], [Row()]);
        await fixture.CompareAsync();
        await using (var repair = fixture.Repair())
        {
            await repair.PrepareAsync(fixture.ReportPath, default); fixture.Activity++;
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => repair.ApplyAsync(default));
        }
        Assert.AreEqual("1", fixture.Data()["Gaps"]);
    }

    // TODO(refactor): Experimental input integrity tests; consolidate report readers after validation.
    [DataTestMethod]
    [DataRow("truncated")]
    [DataRow("count")]
    [DataRow("extra")]
    public async Task InvalidReportCannotStartRepair(string fault)
    {
        using var fixture = new Fixture([], [Row()]);
        await fixture.CompareAsync();
        var lines = File.ReadAllLines(fixture.ReportPath).ToList();
        if (fault == "truncated") lines.RemoveAt(lines.Count - 1);
        else if (fault == "count") lines[^1] = lines[^1].Replace("\"differences\":1", "\"differences\":2");
        else lines.Add(lines[^1]);
        File.WriteAllLines(fixture.ReportPath, lines);
        var before = fixture.Identity;
        await using var repair = fixture.Repair();
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => repair.PrepareAsync(fixture.ReportPath, default));
        Assert.AreEqual(before, fixture.Identity);
    }

    // TODO(refactor): Experimental source identity/lock tests; retain before publishing a repair API.
    [TestMethod]
    public async Task ChangedOrLockedIndexIsRejected()
    {
        using var fixture = new Fixture([], [Row()]);
        var before = fixture.Identity;
        using var directory = FSDirectory.Open(new DirectoryInfo(fixture.IndexPath));
        using var analyzer = new KeywordAnalyzer();
        var writer = new IndexWriter(directory, analyzer, false, IndexWriter.MaxFieldLength.UNLIMITED);
        Assert.ThrowsException<IOException>(() => fixture.Repair(identity: before));
        LuceneRepairWriter.ForceCommit(writer, new Dictionary<string, string>());
        writer.Close();
        Assert.ThrowsException<InvalidDataException>(() => fixture.Repair(identity: before));
    }

    // TODO(refactor): Experimental term parity assertions; retain canonical format/analyzer coverage after refactoring.
    [TestMethod]
    public async Task SerializedTypesAndAnalyzerSettingsProduceExpectedLuceneTerms()
    {
        var row = Row() with { SerializedDocument = JsonSerializer.Serialize(new object[]
        {
            new { Name = "Id", Type = "Int", Value = 1 }, new { Name = "VersionId", Type = "Int", Value = 10 },
            new { Name = "Description", Type = "String", Value = "The Quick Brown Fox" },
            new { Name = "_Text", Type = "String", Value = "The Wild Fox" },
            new { Name = "Label", Type = "String", Value = "Mixed phrase" },
            new { Name = "Words", Type = "String", Value = "Mixed phrase" },
            new { Name = "Strings", Type = "StringArray", Value = new[] { "a", "b" } },
            new { Name = "Flag", Type = "Bool", Value = true },
            new { Name = "Integers", Type = "IntArray", Value = new[] { -2, 7 } },
            new { Name = "LongValue", Type = "Long", Value = 12345678901L },
            new { Name = "FloatValue", Type = "Float", Value = 1.5f },
            new { Name = "DoubleValue", Type = "Double", Value = 2.5 },
            new { Name = "Date", Type = "DateTime", Value = "2026-11-16T12:34:56Z" }
        }) };
        using var fixture = new Fixture([], [row]);
        await fixture.CompareAsync();
        var schema = Fixture.Schema();
        schema.AddDefinition("""
            <ContentType><Fields>
            <Field name="Label" type="ShortText" />
            <Field name="Words" type="ShortText"><Indexing><Analyzer>whitespace</Analyzer></Indexing></Field>
            </Fields></ContentType>
            """);
        await using (var repair = new ComparisonRepairer(fixture.Identity, schema,
            (id, _) => Task.FromResult(fixture.Rows.GetValueOrDefault(id)), _ => Task.FromResult(99)))
        {
            await repair.PrepareAsync(fixture.ReportPath, default); await repair.ApplyAsync(default);
        }
        using var directory = FSDirectory.Open(new DirectoryInfo(fixture.IndexPath));
        using var reader = IndexReader.Open(directory, true);
        foreach (var (field, term) in new[]
        {
            ("Description", "quick"), ("_Text", "wild"), ("Label", "Mixed phrase"), ("Words", "Mixed"), ("Words", "phrase"),
            ("Strings", "a"), ("Strings", "b"), ("Flag", "yes"), ("Integers", NumericUtils.IntToPrefixCoded(-2)),
            ("LongValue", NumericUtils.LongToPrefixCoded(12345678901L)), ("FloatValue", NumericUtils.FloatToPrefixCoded(1.5f)),
            ("DoubleValue", NumericUtils.DoubleToPrefixCoded(2.5)),
            ("Date", NumericUtils.LongToPrefixCoded(new DateTime(2026,11,16,12,34,56,DateTimeKind.Utc).Ticks)),
            ("Depth", NumericUtils.IntToPrefixCoded(1))
        }) Assert.AreEqual(1, reader.DocFreq(new Term(field, term)), field);
        Assert.AreEqual(0, reader.DocFreq(new Term("Description", "The")));
        Assert.AreEqual(0, (await fixture.CompareAsync(false)).Count);
    }

    // TODO(refactor): Experimental Sharing schema parity coverage; retain when extracting shared handler defaults.
    [DataTestMethod]
    [DataRow("SharingIndexHandler")]
    [DataRow("SharedWithIndexHandler")]
    [DataRow("SharedByIndexHandler")]
    [DataRow("SharingModeIndexHandler")]
    [DataRow("SharingLevelIndexHandler")]
    [DataRow(null)]
    public void SharingHandlersResolveKeywordAnalyzer(string? handler)
    {
        var schema = Fixture.Schema();
        var indexing = handler == null ? "" :
            $"<Indexing><IndexHandler>SenseNet.Search.Indexing.{handler}, SenseNet.ContentRepository</IndexHandler></Indexing>";
        schema.AddDefinition($"<ContentType><Fields><Field name=\"Sharing\" type=\"Sharing\">{indexing}</Field></Fields></ContentType>");
        Assert.AreEqual("Keyword", schema.Get("Sharing").Analyzer);
    }

    // TODO(refactor): Experimental Sharing replay regression; retain exact stored-token parity in the supported repair API.
    [DataTestMethod]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public async Task SharingRepairPreservesSerializedTerms(bool hasShares, bool explicitAnalyzer)
    {
        string[] tokens = hasShares
            ? ["Tabc123", "I42", "C7", "M1", "L2", "Tabc123,I42", "I42,C7", "Tabc123,I42,C7,M1,L2"]
            : [];
        var row = Row() with { SerializedDocument = JsonSerializer.Serialize(new object[]
        {
            new { Name = "Id", Type = "Int", Value = 1 },
            new { Name = "VersionId", Type = "Int", Value = 10 },
            new { Name = "Sharing", Type = "StringArray", Mode = "Default", Store = "No", TermVector = "No", Value = tokens }
        }) };
        using var fixture = new Fixture([CheckerTests.Doc(versionTs: 5)], [row]);
        await fixture.CompareAsync();
        var before = fixture.Identity;
        var schema = Fixture.Schema();
        schema.AddDefinition($"""
            <ContentType><Fields><Field name="Sharing" type="Sharing"><Indexing>
            {(explicitAnalyzer ? "<Analyzer>Keyword</Analyzer>" : "")}
            <IndexHandler>SenseNet.Search.Indexing.SharingIndexHandler</IndexHandler>
            </Indexing></Field></Fields></ContentType>
            """);
        await using (var repair = new ComparisonRepairer(fixture.Identity, schema,
            (id, _) => Task.FromResult(fixture.Rows.GetValueOrDefault(id)), _ => Task.FromResult(99)))
        {
            Assert.IsTrue(await repair.PrepareAsync(fixture.ReportPath, default));
            Assert.AreEqual(before, fixture.Identity);
            await repair.ApplyAsync(default);
        }
        using var directory = FSDirectory.Open(new DirectoryInfo(fixture.IndexPath));
        using var reader = IndexReader.Open(directory, true);
        var actual = new List<string>();
        var terms = reader.Terms(new Term("Sharing", ""));
        try
        {
            while (terms.Term() is { } term && term.Field() == "Sharing")
            {
                actual.Add(term.Text());
                Assert.AreEqual(1, reader.DocFreq(term));
                if (!terms.Next()) break;
            }
        }
        finally { terms.Close(); }
        CollectionAssert.AreEquivalent(tokens, actual.ToArray());
        Assert.AreEqual(0, (await fixture.CompareAsync(false)).Count);
        Assert.AreEqual("99", fixture.Data()["LastActivityId"]);
        Assert.IsFalse(fixture.Data().ContainsKey("Gaps"));
    }

    // TODO(refactor): Experimental schema failure/extension tests; retain offline configuration validation.
    [TestMethod]
    public void SchemaResolvesExplicitInheritanceAspectsAndListFieldsAndRejectsUnknownHandlers()
    {
        var schema = Fixture.Schema();
        schema.AddDefinition("""
            <ContentType><Fields><Field name="Description" type="LongText"><Indexing><Analyzer>Keyword</Analyzer></Indexing></Field></Fields></ContentType>
            """);
        Assert.AreEqual("Keyword", schema.Get("Description").Analyzer);
        schema.AddDefinition("""
            <AspectDefinition><Fields><AspectField name="Notes" type="LongText" /></Fields></AspectDefinition>
            """, "Example.");
        Assert.AreEqual("Standard", schema.Get("Example.Notes").Analyzer);
        schema.AddDefinition("""
            <ContentListDefinition><Fields><ContentListField name="#Notes" type="LongText" /></Fields></ContentListDefinition>
            """);
        Assert.AreEqual("Standard", schema.Get("#Notes").Analyzer);
        schema.AddDefinition("""
            <ContentType><Fields><Field name="Custom" type="ShortText"><Indexing><IndexHandler>Example.CustomHandler</IndexHandler></Indexing></Field></Fields></ContentType>
            """);
        Assert.ThrowsException<InvalidDataException>(() => schema.Get("Custom"));
        schema.AddDefinition("""
            <ContentType><Fields><Field name="CustomField" type="ShortText" handler="Example.CustomField" /></Fields></ContentType>
            """);
        Assert.ThrowsException<InvalidDataException>(() => schema.Get("CustomField"));
        Assert.ThrowsException<InvalidDataException>(() => schema.Get("Missing.Aspect"));
        Assert.ThrowsException<InvalidDataException>(() => schema.Get("#Missing"));
    }

    // TODO(refactor): Experimental SQL metadata parity test; retain rejection of inconsistent public version state.
    [TestMethod]
    public void UnapprovedLastPublicVersionAndUnindexedIdentityAreRejected()
    {
        Assert.ThrowsException<InvalidDataException>(() => RepairDocuments.Convert(Row() with { Status = 4 }, Fixture.Schema()));
        var row = Row() with { SerializedDocument = """
            [{"Name":"Id","Type":"Int","Value":1},{"Name":"VersionId","Type":"Int","Mode":"No","Value":10}]
            """ };
        Assert.ThrowsException<InvalidDataException>(() => RepairDocuments.Convert(row, Fixture.Schema()));
    }
}
