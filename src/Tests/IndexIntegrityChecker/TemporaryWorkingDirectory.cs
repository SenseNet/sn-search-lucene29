namespace IndexIntegrityChecker;

/// <summary>Owns exactly one generated child directory in the application's temporary root.</summary>
internal sealed class TemporaryWorkingDirectory : IDisposable
{
    private readonly string _root;
    private readonly string _name;
    public string DirectoryPath { get; }

    public TemporaryWorkingDirectory()
    {
        _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "IndexIntegrityChecker"));
        _name = Guid.NewGuid().ToString("N");
        DirectoryPath = Path.Combine(_root, _name);
        if (Directory.Exists(DirectoryPath) || File.Exists(DirectoryPath))
            throw new IOException("The generated temporary directory already exists.");
        Directory.CreateDirectory(DirectoryPath);
    }

    public void Dispose()
    {
        var fullPath = Path.GetFullPath(DirectoryPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // Never remove the shared temporary root or another run's directory.
        if (!string.Equals(Path.GetDirectoryName(fullPath), _root, comparison) ||
            !string.Equals(Path.GetFileName(fullPath), _name, comparison) || !Guid.TryParseExact(_name, "N", out _))
            throw new IOException("Refusing to remove a path outside the owned temporary directory.");
        if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
    }
}
