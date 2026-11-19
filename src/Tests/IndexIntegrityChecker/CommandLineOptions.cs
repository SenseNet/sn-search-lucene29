using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace IndexIntegrityChecker;

internal sealed record CommandLineOptions(string? ConnectionString, string? IndexPath, string? WorkingDirectory)
{
    public const int DefaultMaxConsoleDifferences = 20;
    public int MaxConsoleDifferences { get; init; } = DefaultMaxConsoleDifferences;

    private static readonly string[] SourceOptions = { "--connection-string", "--index" };

    // TODO(refactor): Experimental CLI normalization; consolidate the command parsing contract after validation.
    public static bool TryParse(string[] args, [NotNullWhen(true)] out CommandLineOptions? options, out string? error)
    {
        options = null;
        error = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var separator = args[i].IndexOf('=');
            var name = separator < 0 ? args[i] : args[i][..separator];
            var canonicalName = name.ToLowerInvariant() switch
            {
                "--connection-string" or "--database" or "--db" or "-d" => "--connection-string",
                "--index" or "-i" => "--index",
                "--working-directory" or "--workdir" or "-w" => "--working-directory",
                "--max-console-differences" => "--max-console-differences",
                _ => null
            };
            if (canonicalName == null)
            {
                // An unexpected token may be a positional connection string. Never echo it.
                error = "Unknown option or positional argument. Use the named options shown below.";
                return false;
            }
            if (values.ContainsKey(canonicalName))
            {
                error = $"Option {canonicalName} was specified more than once (including aliases).";
                return false;
            }

            string value;
            if (separator >= 0) value = args[i][(separator + 1)..];
            else
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                {
                    error = $"Missing value for {canonicalName}.";
                    return false;
                }
                value = args[++i];
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                error = $"Missing value for {canonicalName}.";
                return false;
            }
            values.Add(canonicalName, value);
        }

        var maxConsoleDifferences = DefaultMaxConsoleDifferences;
        if (values.TryGetValue("--max-console-differences", out var limit))
        {
            if (values.ContainsKey("--working-directory"))
            {
                error = "--max-console-differences is only available without -w (temporary mode).";
                return false;
            }
            if (!int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out maxConsoleDifferences)
                || maxConsoleDifferences < 1)
            {
                error = "--max-console-differences must be an integer from 1 to 2147483647.";
                return false;
            }
        }

        var missing = values.ContainsKey("--working-directory")
            ? Array.Empty<string>() : SourceOptions.Where(name => !values.ContainsKey(name)).ToArray();
        if (missing.Length > 0)
        {
            error = "Missing required option(s): " + string.Join(", ", missing) + ".";
            return false;
        }
        options = new(values.GetValueOrDefault("--connection-string"), values.GetValueOrDefault("--index"), values.GetValueOrDefault("--working-directory"))
        { MaxConsoleDifferences = maxConsoleDifferences };
        return true;
    }

    // TODO(refactor): Experimental repair CLI guidance; finalize usage text with the supported workflow contract.
    public static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Temporary: IndexIntegrityChecker --connection-string \"<connectionstring>\" --index \"<path>\" [--max-console-differences <N>]");
        writer.WriteLine("Persistent: IndexIntegrityChecker --connection-string \"<connectionstring>\" --index \"<path>\" --working-directory \"<path>\"");
        writer.WriteLine("Resume: IndexIntegrityChecker --working-directory \"<path>\"");
        writer.WriteLine("Options (case-insensitive names, any order; use <option> <value> or <option>=<value>):");
        writer.WriteLine("  --connection-string, --database, --db, -d   SQL Server connection string");
        writer.WriteLine("  --index, -i                                Lucene index directory");
        writer.WriteLine("  --working-directory, --workdir, -w          Optional persistent workflow directory");
        writer.WriteLine($"  --max-console-differences                  Positive integer; default {DefaultMaxConsoleDifferences}; only without -w");
        writer.WriteLine("Without -w: only the first N differences go to stdout, followed by the complete summary; temporary files are removed.");
        writer.WriteLine("The output limit also applies when stdout is redirected. Use -w to retain all differences.");
        writer.WriteLine("With -w: checkpoints and the report are retained; prepared repair can resume using -w only.");
        writer.WriteLine("Repair uses SQL SELECT only and changes the index in place after interactive confirmation (yes or y).");
        writer.WriteLine("Once repair starts it cannot resume: interruption requires a new full check in a new workspace.");
        writer.WriteLine("Connection string and index path are required when starting a new check.");
        writer.WriteLine("Exit codes: 0 = no repair needed or repair completed, 1 = unconfirmed differences/repair, 2 = error, 130 = cancelled.");
    }
}
