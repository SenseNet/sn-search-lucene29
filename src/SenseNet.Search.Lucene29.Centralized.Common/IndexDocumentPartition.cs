using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace SenseNet.Search.Lucene29.Centralized.Common;

/// <summary>
/// Represents a slice of an IndexDocument in order to send parts of a larger document in a Grpc message.
/// </summary>
public class IndexDocumentPartition
{
    /// <summary>
    /// Gets or sets the VersionId of the IndexDocument.
    /// </summary>
    public int VersionId { get; set; }

    /// <summary>
    /// Gets or sets the zero based index of the partition.
    /// </summary>
    public int PartitionIndex { get; set; }

    /// <summary>
    /// Gets or sets a value which indicates that the current partition is last or not.
    /// </summary>
    public bool IsLast { get; set; }

    /// <summary>
    /// Gets or sets a slice of the serialized IndexDocument.
    /// </summary>
    public string Payload { get; set; }

    /// <summary>
    /// Serializes this instance
    /// </summary>
    /// <returns></returns>
    public string Serialize()
    {
        //return JsonConvert.SerializeObject(this, Formatting.Indented);
        return $"IndexDocumentPartition.VersionId={VersionId},PartitionIndex={PartitionIndex},IsLast={IsLast}|{Payload}";
    }

    /// <summary>
    /// Returns a deserialized IndexDocumentPartition
    /// </summary>
    /// <param name="data">Serialized data</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">Throws an exception if the deserialization is not possible.</exception>
    public static IndexDocumentPartition Deserialize(string data)
    {
        var p = data.IndexOf('|');
        if (p < 23 || !data.StartsWith("IndexDocumentPartition."))
            throw new InvalidOperationException("Unknown data");

        try
        {
            var head = data.Substring(23, p - 23);
            var headParts = head.Split(',').Select(x => x.Split('=')).ToArray();
            var headData = headParts.ToDictionary(h => h[0], h => h[1]);
            var result = new IndexDocumentPartition
            {
                VersionId = int.Parse(headData["VersionId"]),
                PartitionIndex = int.Parse(headData["PartitionIndex"]),
                IsLast = bool.Parse(headData["IsLast"]),
                Payload = p < data.Length ? data.Substring(p + 1) : string.Empty
            };
            return result;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Cannot deserialize data.", e);
        }
    }
}