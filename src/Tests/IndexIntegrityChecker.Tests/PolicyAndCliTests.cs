using System.Text;
using Lucene.Net.Analysis;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IndexIntegrityChecker.Tests;

[TestClass]
public class PolicyAndCliTests
{
    [DataTestMethod]
    [DataRow("false", false)] [DataRow("no", false)] [DataRow("0", false)]
    [DataRow("true", true)] [DataRow("yes", true)] [DataRow("1", true)]
    public void ParsesContentTypeAllowIndexing(string text, bool expected)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"<ContentType xmlns='http://schemas.sensenet.com/SenseNet/ContentRepository/ContentTypeDefinition'><AllowIndexing> {text} </AllowIndexing></ContentType>"));
        Assert.AreEqual(expected, ContentTypePolicy.ReadAllowIndexing(stream));
    }

    [TestMethod]
    public void ExclusionsRespectInheritanceAndExplicitReenable()
    {
        var types = new[] { new NodeTypeDefinition(1, null, "Root"), new NodeTypeDefinition(2, 1, "Disabled"),
            new NodeTypeDefinition(3, 2, "Inherited"), new NodeTypeDefinition(4, 3, "Reenabled") };
        var definitions = new Dictionary<string, bool?> { ["Disabled"] = false, ["Inherited"] = null, ["Reenabled"] = true };
        CollectionAssert.AreEquivalent(new[] { 2, 3 }, ContentTypePolicy.GetExcluded(types, definitions).ToArray());
    }

    [TestMethod]
    public void InvalidContentTypeHierarchyFails()
    {
        Assert.ThrowsException<InvalidDataException>(() => ContentTypePolicy.GetExcluded(
            new[] { new NodeTypeDefinition(1, 2, "A"), new NodeTypeDefinition(2, 1, "B") }, new Dictionary<string, bool?>()));
    }

    [TestMethod]
    public void MissingAllowIndexingInherits()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<ContentType name='Example'><Fields /></ContentType>"));
        Assert.IsNull(ContentTypePolicy.ReadAllowIndexing(stream));
    }

    [TestMethod]
    public async Task CliRejectsMissingAndPositionalArguments()
    {
        Assert.AreEqual(2, await Program.Main(Array.Empty<string>()));
        Assert.AreEqual(2, await Program.Main(new[] { "a" }));
        Assert.AreEqual(2, await Program.Main(new[] { "a", "b", "c" }));
    }

    [TestMethod]
    public void ErrorMessagesRedactConnectionStringAndPassword()
    {
        var connection = "Server=localhost;Database=example;User ID=example;Password=secret123";
        var text = Program.SanitizeError(new Exception(connection + " secret123"), connection);
        Assert.IsFalse(text.Contains("secret123", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("User ID", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ResolvesDirectIndexOrNewestValidChildWithoutWritingToIndex()
    {
        var path = Path.Combine(Path.GetTempPath(), "IndexIntegrityChecker.Tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(path);
        try
        {
            foreach (var name in new[] { "20260101", "20260201" })
            {
                var child = System.IO.Directory.CreateDirectory(Path.Combine(path, name));
                using var directory = FSDirectory.Open(child);
                var writer = new IndexWriter(directory, new KeywordAnalyzer(), true, IndexWriter.MaxFieldLength.UNLIMITED);
                writer.AddDocument(CheckerTests.Doc()); writer.Close();
            }
            System.IO.Directory.CreateDirectory(Path.Combine(path, "zzz-not-an-index"));
            var before = System.IO.Directory.GetFiles(path, "*", SearchOption.AllDirectories).Order().ToArray();
            var expected = Path.Combine(path, "20260201");
            Assert.AreEqual(expected, IndexPathResolver.Resolve(path));
            Assert.AreEqual(expected, IndexPathResolver.Resolve(expected));
            CollectionAssert.AreEqual(before, System.IO.Directory.GetFiles(path, "*", SearchOption.AllDirectories).Order().ToArray());
        }
        finally { System.IO.Directory.Delete(path, true); }
    }
}
