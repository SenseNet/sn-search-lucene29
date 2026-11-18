using Lucene.Net.Index;
using Lucene.Net.Store;

namespace IndexIntegrityChecker;

public static class IndexPathResolver
{
    public static string Resolve(string path)
    {
        var root = new DirectoryInfo(Path.GetFullPath(path));
        if (!root.Exists) throw new DirectoryNotFoundException($"Index directory not found: {root.FullName}");
        if (ContainsIndex(root)) return root.FullName;

        // SenseNet commonly stores committed indexes in timestamp-named child folders.
        foreach (var child in root.EnumerateDirectories().OrderByDescending(x => x.Name, StringComparer.Ordinal))
            if (ContainsIndex(child)) return child.FullName;
        throw new InvalidDataException($"No committed Lucene index found in {root.FullName} or its immediate subdirectories.");
    }

    private static bool ContainsIndex(DirectoryInfo path)
    {
        using var directory = FSDirectory.Open(path);
        return IndexReader.IndexExists(directory);
    }
}
