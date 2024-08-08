using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.GrpcClient;
using SenseNet.Search.Lucene29.Centralized.GrpcService;
using SenseNet.Search.Querying;
using SenseNet.Testing;
using SenseNet.Tools;
using static SenseNet.ApplicationModel.N;

namespace SenseNet.Search.Lucene29.Centralized.Tests;

[TestClass]
public class GrpcPartitioningTests
{
    private class TestGrpcSearchClient : GrpcSearch.GrpcSearchClient
    {
        public readonly List<WriteIndexRequest> Requests = new();

        public override AsyncUnaryCall<WriteIndexResponse> WriteIndexAsync(WriteIndexRequest request, Metadata headers = null, DateTime? deadline = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Requests.Add(request);
            return new AsyncUnaryCall<WriteIndexResponse>(Task.FromResult(new WriteIndexResponse()), null, null, null, null);
        }
    }

    private void CreateInfrastructure(int maxSendMessageSize, out GrpcServiceClient serviceClient, out TestGrpcSearchClient testGrpcSearchClient)
    {
        var grpcClientOptions = new GrpcClientOptions { ChannelOptions = new GrpcChannelOptions { MaxSendMessageSize = maxSendMessageSize } };
        var grpcOptions = new OptionsWrapper<GrpcClientOptions>(grpcClientOptions);
        var grpcLogger = NullLoggerFactory.Instance.CreateLogger<GrpcServiceClient>();

        var retrierOptions = new OptionsWrapper<RetrierOptions>(new RetrierOptions { Count = 3, WaitMilliseconds = 10 });
        var retrierLogger = NullLoggerFactory.Instance.CreateLogger<DefaultRetrier>();
        var retrier = new DefaultRetrier(retrierOptions, retrierLogger);

        serviceClient = new GrpcServiceClient(retrier, grpcOptions, grpcLogger);
        var serviceClientAcc = new ObjectAccessor(serviceClient);
        testGrpcSearchClient = new TestGrpcSearchClient();
        serviceClientAcc.SetField("_searchClient", testGrpcSearchClient);
    }

    [TestMethod]
    public void GrpcPartitioning_Deletions()
    {
        var maxSendMessageSize = 200;
        var maxSendMessageSizeEffective = maxSendMessageSize * 9 / 10;
        CreateInfrastructure(maxSendMessageSize, out var serviceClient, out var testGrpcSearchClient);

        // ACT
        var deletions = Enumerable.Range(0, 10).Select(i => new SnTerm("F1", i)).ToArray();
        serviceClient.WriteIndex(deletions, null, null);

        // ASSERT
        var partitions = testGrpcSearchClient.Requests.Select(x=>x.Deletions.ToArray()).ToArray();
        var expectedTotalLength = deletions.Sum(x => x.Serialize().Length);
        var totalLength = partitions.SelectMany(x => x).Sum(x => x.Length);
        Assert.AreEqual(expectedTotalLength, totalLength);

        for (int i = 0; i < partitions.Length; i++)
        {
            var request = partitions[i];
            var length = request.Sum(x => x.Length);
            Assert.IsTrue(length < maxSendMessageSizeEffective, $"Request {i} too long: {length}. Expected max: {maxSendMessageSizeEffective}");
        }
    }
    [TestMethod]
    public void GrpcPartitioning_Updates()
    {
        var maxSendMessageSize = 1_500;
        var maxSendMessageSizeEffective = maxSendMessageSize * 9 / 10;
        CreateInfrastructure(maxSendMessageSize, out var serviceClient, out var testGrpcSearchClient);

        // ACT
        var updates = Enumerable.Range(0, 10).Select(i => new DocumentUpdate
        {
            UpdateTerm = new SnTerm("String1", "Value" + i),
            Document = new IndexDocument
            {
                new IndexField("String1", "value" + i, IndexingMode.Analyzed, IndexStoringMode.Default,
                    IndexTermVector.Default),
                new IndexField("Integer1", i, IndexingMode.No, IndexStoringMode.Yes,
                    IndexTermVector.Default),
            }
        }).ToArray();
        serviceClient.WriteIndex(null, updates, null);

        // ASSERT
        var partitions = testGrpcSearchClient.Requests.Select(x => x.Updates.ToArray()).ToArray();
        var expectedTotalLength = updates.Sum(x => x.Serialize().Length);
        var totalLength = partitions.SelectMany(x => x).Sum(x => x.Length);
        Assert.AreEqual(expectedTotalLength, totalLength);

        for (int i = 0; i < partitions.Length; i++)
        {
            var request = partitions[i];
            var length = request.Sum(x => x.Length);
            Assert.IsTrue(length < maxSendMessageSizeEffective, $"Request {i} too long: {length}. Expected max: {maxSendMessageSizeEffective}");
        }
    }
    [TestMethod]
    public void GrpcPartitioning_Additions()
    {
        var maxSendMessageSize = 5_000;
        var maxSendMessageSizeEffective = maxSendMessageSize * 9 / 10;
        CreateInfrastructure(maxSendMessageSize, out var serviceClient, out var testGrpcSearchClient);

        // ACT
        var additions = Enumerable.Range(0, 10).Select(i => new IndexDocument
        {
            new IndexField("Name", "Content" + i, IndexingMode.Default, IndexStoringMode.Default, IndexTermVector.Default),
            new IndexField("String1", "value", IndexingMode.Default, IndexStoringMode.Default, IndexTermVector.Default),
            new IndexField("StringArray1", new[] {"value1", "value2"}, IndexingMode.Analyzed, IndexStoringMode.No, IndexTermVector.No),
            new IndexField("Boolean1", true, IndexingMode.AnalyzedNoNorms, IndexStoringMode.Yes, IndexTermVector.WithOffsets),
            new IndexField("Integer1", 42, IndexingMode.No, IndexStoringMode.Default, IndexTermVector.WithPositions),
            new IndexField("IntegerArray1", new[] {42, 43, 44}, IndexingMode.NotAnalyzed, IndexStoringMode.Default, IndexTermVector.WithPositions),
            new IndexField("Long1", 42L, IndexingMode.Analyzed, IndexStoringMode.Default, IndexTermVector.WithPositionsOffsets),
            new IndexField("Float1", (float) 123.45, IndexingMode.NotAnalyzed, IndexStoringMode.Default, IndexTermVector.Yes),
            new IndexField("Double1", 123.45d, IndexingMode.NotAnalyzedNoNorms, IndexStoringMode.Default, IndexTermVector.Default),
            new IndexField("DateTime1", new DateTime(2019, 04, 19, 9, 38, 15), IndexingMode.Default, IndexStoringMode.Default, IndexTermVector.Default)
        }).ToArray();
        serviceClient.WriteIndex(null, null, additions);

        // ASSERT
        var partitions = testGrpcSearchClient.Requests.Select(x => x.Additions.ToArray()).ToArray();
        var expectedTotalLength = additions.Sum(x => x.Serialize().Length);
        var totalLength = partitions.SelectMany(x => x).Sum(x => x.Length);
        Assert.AreEqual(expectedTotalLength, totalLength);

        for (int i = 0; i < partitions.Length; i++)
        {
            var request = partitions[i];
            var length = request.Sum(x => x.Length);
            Assert.IsTrue(length < maxSendMessageSizeEffective, $"Request {i} too long: {length}. Expected max: {maxSendMessageSizeEffective}");
        }
    }

    [TestMethod]
    public void GrpcPartitioning_Updates_Big()
    {
        var maxSendMessageSize = 1_500;
        var maxSendMessageSizeEffective = maxSendMessageSize * 9 / 10;
        CreateInfrastructure(maxSendMessageSize, out var serviceClient, out var testGrpcSearchClient);

        // ACT
        var updates = Enumerable.Range(0, 10).Select(i => new DocumentUpdate
        {
            UpdateTerm = new SnTerm("String1", "Value" + i),
            Document = new IndexDocument
            {
                new IndexField("VersionId", 42, IndexingMode.Default, IndexStoringMode.Default, IndexTermVector.Default),
                new IndexField("String1", "value" + i, IndexingMode.Analyzed, IndexStoringMode.Default,
                    IndexTermVector.Default),
                new IndexField("Binary", "Small binary", IndexingMode.Default, IndexStoringMode.Default,
                    IndexTermVector.Default),
                new IndexField("Integer1", i, IndexingMode.No, IndexStoringMode.Yes,
                    IndexTermVector.Default),
                new IndexField("_Text", "Small binary", IndexingMode.No, IndexStoringMode.Yes,
                    IndexTermVector.Default),
            }
        }).ToArray();

        var bigData = string.Join(" ", Enumerable.Range(0, 500).Select(i => "xxxxxxxxxx" + i));
        updates[5].Document.Fields["Binary"] = new IndexField("Binary", bigData, IndexingMode.Default,
            IndexStoringMode.Default, IndexTermVector.Default);
        updates[5].Document.Fields["_Text"] = new IndexField("_Text", bigData, IndexingMode.Default,
            IndexStoringMode.Default, IndexTermVector.Default);

        serviceClient.WriteIndex(null, updates, null);

        // ASSERT
        var partitions = testGrpcSearchClient.Requests.Select(x => x.Updates.ToArray()).ToArray();
        var expectedTotalLength = updates.Sum(x => x.Serialize().Length);
        var totalLength = partitions.SelectMany(x => x).Sum(x => x.Length);
        Assert.AreEqual(expectedTotalLength, totalLength);

        for (int i = 0; i < partitions.Length; i++)
        {
            var request = partitions[i];
            var length = request.Sum(x => x.Length);
            Assert.IsTrue(length < maxSendMessageSizeEffective, $"Request {i} too long: {length}. Expected max: {maxSendMessageSizeEffective}");
        }
    }
    [TestMethod]
    public void GrpcPartitioning_Additions_Big_MoreFields()
    {
        string GetStringValue(int length)
        {
            var s = string.Join(" ", Enumerable.Range(0, length / 10).Select(i => "xxxxxxxxx")) + ".";
            var l = s.Length;
            return s;
        }

        var maxSendMessageSize = 2_200;
        var maxSendMessageSizeEffective = maxSendMessageSize * 9 / 10;
        CreateInfrastructure(maxSendMessageSize, out var serviceClient, out var testGrpcSearchClient);

        // ACT
        var additions = Enumerable.Range(0, 10).Select(i => new IndexDocument
        {
            new IndexField("Name", "Content" + i, IndexingMode.Default, IndexStoringMode.Default, IndexTermVector.Default),
            new IndexField("VersionId", 42 + i, IndexingMode.Default, IndexStoringMode.Default, IndexTermVector.Default),
            new IndexField("Binary", "Small binary", IndexingMode.Default, IndexStoringMode.Default, IndexTermVector.Default),
            new IndexField("String1", "value", IndexingMode.Default, IndexStoringMode.Default, IndexTermVector.Default),
            new IndexField("StringArray1", new[] {"value1", "value2"}, IndexingMode.Analyzed, IndexStoringMode.No, IndexTermVector.No),
            new IndexField("Boolean1", true, IndexingMode.AnalyzedNoNorms, IndexStoringMode.Yes, IndexTermVector.WithOffsets),
            new IndexField("Integer1", 42, IndexingMode.No, IndexStoringMode.Default, IndexTermVector.WithPositions),
            new IndexField("IntegerArray1", new[] {42, 43, 44}, IndexingMode.NotAnalyzed, IndexStoringMode.Default, IndexTermVector.WithPositions),
            new IndexField("Long1", 42L, IndexingMode.Analyzed, IndexStoringMode.Default, IndexTermVector.WithPositionsOffsets),
            new IndexField("Float1", (float) 123.45, IndexingMode.NotAnalyzed, IndexStoringMode.Default, IndexTermVector.Yes),
            new IndexField("_Text", "Small binary", IndexingMode.No, IndexStoringMode.Yes, IndexTermVector.Default),
            new IndexField("Double1", 123.45d, IndexingMode.NotAnalyzedNoNorms, IndexStoringMode.Default, IndexTermVector.Default),
            new IndexField("DateTime1", new DateTime(2019, 04, 19, 9, 38, 15), IndexingMode.Default, IndexStoringMode.Default, IndexTermVector.Default)
        }).ToArray();
        additions[5] = new IndexDocument
        {
            new("Name", "Content" + 5, 0, 0, 0),
            new("VersionId", 42 + 5, 0, 0, 0),
            new("String01", GetStringValue(100), 0, 0, 0),
            new("String02", GetStringValue(100), 0, 0, 0),
            new("String03", GetStringValue(100), 0, 0, 0),
            new("String04", GetStringValue(100), 0, 0, 0),
            new("String05", GetStringValue(200), 0, 0, 0),
            new("String06", GetStringValue(300), 0, 0, 0),
            new("String07", GetStringValue(400), 0, 0, 0),
            new("String08", GetStringValue(500), 0, 0, 0),
            new("String09", GetStringValue(600), 0, 0, 0),
            new("String10", GetStringValue(700), 0, 0, 0),
            new("String11", GetStringValue(800), 0, 0, 0),
            new("String12", GetStringValue(900), 0, 0, 0),
            new("String13", GetStringValue(1000), 0, 0, 0),
        };
        serviceClient.WriteIndex(null, null, additions);

        // ASSERT
        var partitions = testGrpcSearchClient.Requests.Select(x => x.Additions.ToArray()).ToArray();
        var expectedTotalLength = additions.Sum(x => x.Serialize().Length);
        var totalLength = partitions.SelectMany(x => x).Sum(x => x.Length);
        totalLength -= 4 * 77; // total length of the additional common field terms.
        Assert.AreEqual(expectedTotalLength, totalLength);

        for (int i = 0; i < partitions.Length; i++)
        {
            var request = partitions[i];
            var length = request.Sum(x => x.Length);
            Assert.IsTrue(length < maxSendMessageSizeEffective, $"Request {i} too long: {length}. Expected max: {maxSendMessageSizeEffective}");
        }
    }
    [TestMethod]
    public void GrpcPartitioning_Additions_Big_LargeFields()
    {
        var maxSendMessageSize = 5_000;
        var maxSendMessageSizeEffective = maxSendMessageSize * 9 / 10;
        CreateInfrastructure(maxSendMessageSize, out var serviceClient, out var testGrpcSearchClient);

        // ACT
        var additions = Enumerable.Range(0, 10).Select(i => new IndexDocument
        {
            new IndexField("Name", "Content" + i, IndexingMode.Default, IndexStoringMode.Default, IndexTermVector.Default),
            new IndexField("VersionId", 42 + i, IndexingMode.Default, IndexStoringMode.Default, IndexTermVector.Default),
            new IndexField("Binary", "Small binary", IndexingMode.Default, IndexStoringMode.Default, IndexTermVector.Default),
            new IndexField("String1", "value", IndexingMode.Default, IndexStoringMode.Default, IndexTermVector.Default),
            new IndexField("StringArray1", new[] {"value1", "value2"}, IndexingMode.Analyzed, IndexStoringMode.No, IndexTermVector.No),
            new IndexField("Boolean1", true, IndexingMode.AnalyzedNoNorms, IndexStoringMode.Yes, IndexTermVector.WithOffsets),
            new IndexField("Integer1", 42, IndexingMode.No, IndexStoringMode.Default, IndexTermVector.WithPositions),
            new IndexField("IntegerArray1", new[] {42, 43, 44}, IndexingMode.NotAnalyzed, IndexStoringMode.Default, IndexTermVector.WithPositions),
            new IndexField("Long1", 42L, IndexingMode.Analyzed, IndexStoringMode.Default, IndexTermVector.WithPositionsOffsets),
            new IndexField("Float1", (float) 123.45, IndexingMode.NotAnalyzed, IndexStoringMode.Default, IndexTermVector.Yes),
            new IndexField("_Text", "Small binary", IndexingMode.No, IndexStoringMode.Yes, IndexTermVector.Default),
            new IndexField("Double1", 123.45d, IndexingMode.NotAnalyzedNoNorms, IndexStoringMode.Default, IndexTermVector.Default),
            new IndexField("DateTime1", new DateTime(2019, 04, 19, 9, 38, 15), IndexingMode.Default, IndexStoringMode.Default, IndexTermVector.Default)
        }).ToArray();

        var bigData = string.Join(" ", Enumerable.Range(0, 1000).Select(i => "Xxxxxxxxxx" + i));
        additions[5].Fields["Binary"] = new IndexField("Binary", bigData, IndexingMode.Default,
            IndexStoringMode.Default, IndexTermVector.Default);
        additions[5].Fields["_Text"] = new IndexField("_Text", bigData, IndexingMode.Default,
            IndexStoringMode.Default, IndexTermVector.Default);
        var expectedTotalLength = additions.Sum(x =>
        {
            var versionId = x.Fields["VersionId"].IntegerValue;
            var y = new IndexDocument();
            foreach (var item in x.Fields)
                y.Fields.Add(item);
            var s = y.Serialize();
            return s.Length;
        });

        serviceClient.WriteIndex(null, null, additions);

        // ASSERT
        var partitions = testGrpcSearchClient.Requests.Select(x => x.Additions.ToArray()).ToArray();
        var totalLength = partitions.SelectMany(x => x).Sum(x => x.Length);
        expectedTotalLength -= 147; // Original skeleton of the Binary and _Text fields.
        expectedTotalLength += 4 * (151 + 150); // Skeletons of the additional document slices (common field + Binary or _Text)
        expectedTotalLength -= 6; // Dropped whitespaces when the big fields sliced.
        Assert.AreEqual(expectedTotalLength, totalLength);

        for (int i = 0; i < partitions.Length; i++)
        {
            var request = partitions[i];
            var length = request.Sum(x => x.Length);
            Assert.IsTrue(length < maxSendMessageSizeEffective, $"Request {i} too long: {length}. Expected max: {maxSendMessageSizeEffective}");
        }
    }
}