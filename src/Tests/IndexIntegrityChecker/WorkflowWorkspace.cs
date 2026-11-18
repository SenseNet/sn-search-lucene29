using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IndexIntegrityChecker;

internal interface IConnectionProtector
{
    byte[] Protect(string connectionString, Guid workflowId);
    string Unprotect(byte[] ciphertext, Guid workflowId);
}

internal sealed class WindowsConnectionProtector : IConnectionProtector
{
    public byte[] Protect(string connectionString, Guid workflowId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Saving connection settings requires Windows user-scoped DPAPI.");
        var bytes = Encoding.UTF8.GetBytes(connectionString);
        try { return ProtectedData.Protect(bytes, workflowId.ToByteArray(), DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public string Unprotect(byte[] ciphertext, Guid workflowId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Reading saved connection settings requires Windows user-scoped DPAPI.");
        var bytes = ProtectedData.Unprotect(ciphertext, workflowId.ToByteArray(), DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

internal sealed class WorkflowWorkspace : IDisposable
{
    private readonly FileStream _lock;
    public string DirectoryPath { get; }
    public string StatePath => Path.Combine(DirectoryPath, "workflow.json");
    public WorkflowWorkspace(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        Directory.CreateDirectory(DirectoryPath);
        try { _lock = new FileStream(Path.Combine(DirectoryPath, "workflow.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new IOException("The working directory is already in use by another process, or its workflow lock is unavailable."); }
    }

    public string FilePath(string name)
    {
        if (name != Path.GetFileName(name) || string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException("A workflow artifact must be a file in the working directory.");
        return Path.Combine(DirectoryPath, name);
    }

    public WorkflowState? Load()
    {
        if (!File.Exists(StatePath)) return null;
        if (new FileInfo(StatePath).Length > 1024 * 1024) throw new InvalidDataException("workflow.json is too large.");
        var state = JsonSerializer.Deserialize<WorkflowState>(File.ReadAllText(StatePath), JsonOutput.Options)
            ?? throw new InvalidDataException("workflow.json is empty.");
        state.Validate();
        return state;
    }

    public void Save(WorkflowState state)
    {
        state.UpdatedAt = DateTimeOffset.UtcNow;
        state.Validate();
        CheckpointWriter.AtomicWrite(StatePath, JsonSerializer.SerializeToUtf8Bytes(state,
            new JsonSerializerOptions(JsonOutput.Options) { WriteIndented = true }));
    }

    public void Dispose() => _lock.Dispose();
}
