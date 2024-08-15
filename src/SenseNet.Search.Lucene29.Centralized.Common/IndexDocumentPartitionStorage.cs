using System.Collections.Generic;

namespace SenseNet.Search.Lucene29.Centralized.Common;

/// <summary>
/// Defines a temporary storage of the IndexDocument partitions.
/// </summary>
public interface IIndexDocumentPartitionStorage
{
    /// <summary>
    /// Determines whether the storage contains the specified VersionId
    /// </summary>
    /// <param name="versionId">VersionId of the partitions.</param>
    /// <returns>True if the storage contains an element with the specified VersionId; otherwise, false.</returns>
    bool Contains(int versionId);

    /// <summary>
    /// Gets the list of IndexDocumentPartitions associated with the specified VersionId.
    /// </summary>
    /// <param name="versionId"></param>
    /// <param name="partitions">When this method returns, contains the list of IndexDocumentPartitions
    /// associated with the specified VersionId, if the key is found; otherwise null.
    /// This parameter is passed uninitialized.</param>
    /// <returns>True if the storage contains an element associated with the specified VersionId; otherwise, false.</returns>
    bool TryGet(int versionId, out IList<IndexDocumentPartition> partitions);

    /// <summary>
    /// Adds the specified VersionId and IndexDocumentPartition list to the storage.
    /// </summary>
    /// <param name="versionId">VersionId of the partitions.</param>
    /// <param name="partitions">An empty IList&lt;IndexDocumentPartition&gt; instance.</param>
    void Add(int versionId, IList<IndexDocumentPartition> partitions);

    /// <summary>
    /// Removes the list with the specified VersionId from the storage.
    /// </summary>
    /// <param name="versionId">VersionId of the partitions.</param>
    /// <returns>True if the element is successfully found and removed; otherwise, false.</returns>
    bool Remove(int versionId);
}
/// <summary>
/// Default temporary, in-memory storage of the IndexDocument partitions.
/// </summary>
public class IndexDocumentPartitionStorage : IIndexDocumentPartitionStorage
{
    private readonly Dictionary<int, IList<IndexDocumentPartition>> _storage = new();

    /// <inheritdoc />
    public bool Contains(int versionId) => _storage.ContainsKey(versionId);

    /// <inheritdoc />
    public bool TryGet(int versionId, out IList<IndexDocumentPartition> partitions)
        => _storage.TryGetValue(versionId, out partitions);

    /// <inheritdoc />
    public void Add(int versionId, IList<IndexDocumentPartition> partitions) => _storage.Add(versionId, partitions);

    /// <inheritdoc />
    public bool Remove(int versionId) => _storage.Remove(versionId);
}
