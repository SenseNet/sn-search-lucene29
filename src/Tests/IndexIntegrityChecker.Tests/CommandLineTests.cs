using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IndexIntegrityChecker.Tests;

[TestClass]
public class CommandLineTests
{
    private const string Connection = "Server=localhost;Database=example;Password=MixedCase=aB;Integrated Security=false";

    public static IEnumerable<object[]> AcceptedAliases()
    {
        foreach (var database in new[] { "--connection-string", "--database", "--db", "-d" })
        foreach (var index in new[] { "--index", "-i" })
        foreach (var work in new[] { "--working-directory", "--workdir", "-w" })
            yield return new object[] { database, index, work };
    }

    [DataTestMethod]
    [DynamicData(nameof(AcceptedAliases), DynamicDataSourceType.Method)]
    public void AcceptsAllAliasesInArbitraryOrder(string database, string index, string work)
    {
        foreach (var args in new[]
        {
            new[] { work, @"D:\My Reports", database, Connection, index, @"D:\My Index" },
            new[] { work.ToUpperInvariant() + @"=D:\My Reports", database.ToUpperInvariant() + "=" + Connection,
                index.ToUpperInvariant() + @"=D:\My Index" }
        })
        {
            Assert.IsTrue(CommandLineOptions.TryParse(args, out var options, out var error), error);
            Assert.AreEqual(new CommandLineOptions(Connection, @"D:\My Index", @"D:\My Reports"), options);
        }
    }

    [TestMethod]
    public void EqualsSyntaxPreservesAllEqualsSignsInTheValue()
    {
        var valid = CommandLineOptions.TryParse(new[] { "--db=" + Connection, "-i=relative index", "--workdir=relative reports" },
            out var options, out var error);
        Assert.IsTrue(valid, error);
        Assert.AreEqual(Connection, options!.ConnectionString);
        Assert.AreEqual("relative index", options.IndexPath);
        Assert.AreEqual("relative reports", options.WorkingDirectory);
    }

    [DataTestMethod]
    [DataRow("--workingDirectory")]
    [DataRow("--indx")]
    [DataRow("--unknown")]
    [DataRow("--no-persistence")]
    [DataRow("-n")]
    public void RejectsUnacceptedNames(string name)
    {
        Assert.IsFalse(CommandLineOptions.TryParse(new[] { "-d", Connection, "-i", "index", name, "reports" }, out _, out _));
    }

    [DataTestMethod]
    [DataRow("-d", "--database")]
    [DataRow("--index", "-i")]
    [DataRow("--workdir", "--working-directory")]
    [DataRow("--db", "--DB")]
    [DataRow("--INDEX", "-i")]
    [DataRow("--WorkDir", "--WORKING-DIRECTORY")]
    [DataRow("--MAX-CONSOLE-DIFFERENCES", "--max-console-differences")]
    public void RejectsRepeatedOptionsIncludingDifferentAliases(string first, string second)
    {
        Assert.IsFalse(CommandLineOptions.TryParse(new[] { first, "value", second, "another value" }, out _, out var error));
        StringAssert.Contains(error!, "more than once");
    }

    [TestMethod]
    public void AcceptsTemporaryRunsAndResumeWithoutSourceOptions()
    {
        Assert.IsTrue(CommandLineOptions.TryParse(new[] { "-d", Connection, "-i", "index" }, out var temporary, out var error), error);
        Assert.IsNull(temporary!.WorkingDirectory);
        Assert.AreEqual(20, temporary.MaxConsoleDifferences);
        Assert.IsTrue(CommandLineOptions.TryParse(new[] { "-W", "reports" }, out var options, out error), error);
        Assert.IsNull(options!.ConnectionString);
        Assert.IsNull(options.IndexPath);
        Assert.AreEqual("reports", options.WorkingDirectory);
        Assert.AreEqual(20, options.MaxConsoleDifferences);
    }

    [TestMethod]
    public void MixedCaseNamesAreCultureIndependentAndPreserveValues()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.IsTrue(CommandLineOptions.TryParse(new[] { "--InDeX=D:\\My Index", "--DaTaBaSe", Connection,
                "--MaX-CoNsOlE-DiFfErEnCeS=50" }, out var options, out var error), error);
            Assert.AreEqual(Connection, options!.ConnectionString);
            Assert.AreEqual(@"D:\My Index", options.IndexPath);
            Assert.AreEqual(50, options.MaxConsoleDifferences);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [TestMethod]
    public void TemporaryModeRequiresBothSourceOptions()
    {
        Assert.IsFalse(CommandLineOptions.TryParse(Array.Empty<string>(), out _, out _));
        Assert.IsFalse(CommandLineOptions.TryParse(new[] { "-d", Connection }, out _, out var error));
        StringAssert.Contains(error!, "--index");
        Assert.IsFalse(CommandLineOptions.TryParse(new[] { "-i", "index" }, out _, out error));
        StringAssert.Contains(error!, "--connection-string");
        Assert.IsTrue(CommandLineOptions.TryParse(new[] { "--db=" + Connection, "-i=relative index" },
            out var options, out error), error);
        Assert.AreEqual(new CommandLineOptions(Connection, "relative index", null), options);
    }

    [TestMethod]
    public void DetectsMissingAndEmptyValues()
    {
        Assert.IsFalse(CommandLineOptions.TryParse(new[] { "-d" }, out _, out _));
        Assert.IsFalse(CommandLineOptions.TryParse(new[] { "-d", "-i", "index", "-w", "reports" }, out _, out _));
        Assert.IsFalse(CommandLineOptions.TryParse(new[] { "--db=", "-i", "index", "-w", "reports" }, out _, out _));
        Assert.IsFalse(CommandLineOptions.TryParse(new[] { "-d", Connection, "-i", "index", "-w", " " }, out _, out _));
    }

    [TestMethod]
    public void ArgumentErrorsNeverEchoConnectionStringOrUnknownTokens()
    {
        Assert.IsFalse(CommandLineOptions.TryParse(new[] { Connection, "index", "reports" }, out _, out var error));
        Assert.IsFalse(error!.Contains(Connection, StringComparison.Ordinal));
        Assert.IsFalse(error.Contains("Password", StringComparison.Ordinal));
        Assert.IsFalse(CommandLineOptions.TryParse(new[] { "--secret-option=" + Connection }, out _, out error));
        Assert.IsFalse(error!.Contains(Connection, StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow("1", 1)]
    [DataRow("50", 50)]
    [DataRow("2147483647", int.MaxValue)]
    public void AcceptsConsoleLimitWithBothSyntaxesAndArgumentOrders(string value, int expected)
    {
        foreach (var args in new[]
        {
            new[] { "--max-console-differences", value, "-i", "index", "-d", Connection },
            new[] { "-d", Connection, "-i", "index", "--MAX-CONSOLE-DIFFERENCES=" + value }
        })
        {
            Assert.IsTrue(CommandLineOptions.TryParse(args, out var options, out var error), error);
            Assert.AreEqual(expected, options!.MaxConsoleDifferences);
            Assert.IsNull(options.WorkingDirectory);
        }
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("1.5")]
    [DataRow("1,000")]
    [DataRow("2147483648")]
    [DataRow(Connection)]
    public void RejectsInvalidConsoleLimitsWithoutEchoingValues(string value)
    {
        Assert.IsFalse(CommandLineOptions.TryParse(
            new[] { "-d", Connection, "-i", "index", "--max-console-differences=" + value },
            out _, out var error));
        StringAssert.Contains(error!, "--max-console-differences");
        Assert.IsFalse(error.Contains(Connection, StringComparison.Ordinal));
    }

    [TestMethod]
    public void RejectsMissingOrRepeatedConsoleLimits()
    {
        foreach (var tail in new[]
        {
            new[] { "--max-console-differences" },
            new[] { "--max-console-differences", "--unknown" },
            new[] { "--max-console-differences", "1", "--max-console-differences=2" }
        })
        {
            Assert.IsFalse(CommandLineOptions.TryParse(
                new[] { "-d", Connection, "-i", "index" }.Concat(tail).ToArray(), out _, out var error));
            StringAssert.Contains(error!, "--max-console-differences");
        }
    }

    [DataTestMethod]
    [DataRow("-w")]
    [DataRow("--workdir")]
    [DataRow("--working-directory")]
    [DataRow("-W")]
    [DataRow("--WorkDir")]
    public void RejectsConsoleLimitForPersistentStartsAndResumes(string workOption)
    {
        foreach (var start in new[] { false, true })
        foreach (var limitFirst in new[] { false, true })
        {
            var sources = start ? new[] { "-d", Connection, "-i", "index" } : Array.Empty<string>();
            var work = new[] { workOption, "reports" };
            var limit = new[] { "--MAX-CONSOLE-DIFFERENCES", "20" };
            var args = sources.Concat(limitFirst ? limit.Concat(work) : work.Concat(limit)).ToArray();
            Assert.IsFalse(CommandLineOptions.TryParse(args, out _, out var error));
            StringAssert.Contains(error!, "only available without -w");
        }
    }

    [TestMethod]
    public void UsageExplainsTemporaryOutputLimitAndPersistence()
    {
        using var usage = new StringWriter();
        CommandLineOptions.WriteUsage(usage);
        StringAssert.Contains(usage.ToString(), "--max-console-differences");
        StringAssert.Contains(usage.ToString(), "default 20; only without -w");
        StringAssert.Contains(usage.ToString(), "stdout is redirected");
        StringAssert.Contains(usage.ToString(), "Use -w to retain all differences.");
    }

    [TestMethod]
    public void AFileCannotBeUsedAsTheWorkingDirectory()
    {
        var path = Path.GetTempFileName();
        try { Assert.ThrowsException<IOException>(() => new WorkflowWorkspace(path)); }
        finally { File.Delete(path); }
    }
}
