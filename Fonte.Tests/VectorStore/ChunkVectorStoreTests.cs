using System.Globalization;
using Fonte.Api.Embeddings;
using Fonte.Api.Indexing;
using Fonte.Api.VectorStore;
using Microsoft.Extensions.Options;

namespace Fonte.Tests.VectorStore;

public class ChunkVectorStoreTests
{
    private const string Alias = "fonte-chunks";
    private const int Dimensions = 128;
    private const string NewCollection = "fonte-chunks-20260926153045123";
    private const string ActiveCollection = "fonte-chunks-20260920100000000";
    private const string LeftoverCollection = "fonte-chunks-20260925120000000";

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 15, 30, 45, 123, TimeSpan.Zero);

    private static readonly EmbeddedChunk[] TwoDocuments =
    [
        Embedded("a.md", 0, 0.1f),
        Embedded("a.md", 1, 0.2f),
        Embedded("guias/b.md", 0, 0.3f),
    ];

    private static ChunkVectorStore CreateStore(FakeQdrantGateway gateway) =>
        new(
            gateway,
            Options.Create(new QdrantOptions { CollectionName = Alias }),
            Options.Create(new GeminiOptions { EmbeddingDimensions = Dimensions }),
            new FixedTimeProvider(Now));

    private static EmbeddedChunk Embedded(string documentPath, int index, float value) =>
        new(new DocumentChunk(documentPath, index, $"{documentPath} #{index}"), Enumerable.Repeat(value, Dimensions).ToArray());

    [Fact]
    public async Task CreatesNewCollectionWithConfiguredDimensionsAndCosine()
    {
        var gateway = new FakeQdrantGateway();

        await CreateStore(gateway).ReplaceAllAsync(TwoDocuments);

        Assert.Contains($"create {NewCollection} {Dimensions} Cosine", gateway.Operations);
    }

    [Fact]
    public async Task CollectionNameUsesGregorianTimestampRegardlessOfCulture()
    {
        var gateway = new FakeQdrantGateway();
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("th-TH");

        try
        {
            await CreateStore(gateway).ReplaceAllAsync(TwoDocuments);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Contains($"create {NewCollection} {Dimensions} Cosine", gateway.Operations);
    }

    [Fact]
    public async Task CreatesKeywordIndexOnDocumentPathAfterCreatingCollection()
    {
        var gateway = new FakeQdrantGateway();

        await CreateStore(gateway).ReplaceAllAsync(TwoDocuments);

        Assert.True(
            gateway.IndexOf($"create {NewCollection} {Dimensions} Cosine") < gateway.IndexOf($"index {NewCollection} document_path"));
    }

    [Fact]
    public async Task UpsertsOneCallPerDocumentWithIdsVectorsAndPayload()
    {
        var gateway = new FakeQdrantGateway();

        await CreateStore(gateway).ReplaceAllAsync(TwoDocuments);

        Assert.Equal(
            [$"upsert {NewCollection} a.md,a.md", $"upsert {NewCollection} guias/b.md"],
            gateway.Operations.Where(o => o.StartsWith("upsert")));

        var points = gateway.Upserted.SelectMany(p => p).ToList();
        Assert.Equal(TwoDocuments.Length, points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var chunk = TwoDocuments[i].Chunk;
            Assert.Equal(ChunkPointId.For(chunk.DocumentPath, chunk.Index), points[i].Id);
            Assert.Equal(chunk.DocumentPath, points[i].DocumentPath);
            Assert.Equal(chunk.Index, points[i].ChunkIndex);
            Assert.Equal(chunk.Content, points[i].Content);
            Assert.Equal(TwoDocuments[i].Vector.ToArray(), points[i].Vector.ToArray());
        }
    }

    [Fact]
    public async Task SwitchesAliasOnlyAfterAllUpserts()
    {
        var gateway = new FakeQdrantGateway { AliasTarget = ActiveCollection, Collections = { ActiveCollection } };

        await CreateStore(gateway).ReplaceAllAsync(TwoDocuments);

        Assert.True(gateway.LastIndexOf("upsert") < gateway.IndexOf($"switch {Alias} {NewCollection} replace=True"));
    }

    [Fact]
    public async Task UpsertFailureKeepsAliasAndCollectionsAndPropagatesError()
    {
        var failure = new InvalidOperationException("falha no upsert");
        var gateway = new FakeQdrantGateway
        {
            AliasTarget = ActiveCollection,
            Collections = { ActiveCollection, LeftoverCollection },
            FailUpsertOf = ("guias/b.md", failure),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateStore(gateway).ReplaceAllAsync(TwoDocuments));

        Assert.Same(failure, exception);
        Assert.DoesNotContain(gateway.Operations, o => o.StartsWith("switch") || o.StartsWith("delete"));
        Assert.Equal(ActiveCollection, gateway.AliasTarget);
    }

    [Fact]
    public async Task DeletesPreviousActiveCollectionOnlyAfterSwitch()
    {
        var gateway = new FakeQdrantGateway { AliasTarget = ActiveCollection, Collections = { ActiveCollection } };

        await CreateStore(gateway).ReplaceAllAsync(TwoDocuments);

        Assert.True(gateway.IndexOf($"switch {Alias} {NewCollection} replace=True") < gateway.IndexOf($"delete {ActiveCollection}"));
    }

    [Fact]
    public async Task WithoutAliasCreatesItWithoutReplacingAnything()
    {
        var gateway = new FakeQdrantGateway();

        await CreateStore(gateway).ReplaceAllAsync(TwoDocuments);

        Assert.Contains($"switch {Alias} {NewCollection} replace=False", gateway.Operations);
        Assert.DoesNotContain(gateway.Operations, o => o.StartsWith("delete"));
    }

    [Fact]
    public async Task DeletesLeftoverAndPreviousActiveButNeverNewCollection()
    {
        var gateway = new FakeQdrantGateway
        {
            AliasTarget = ActiveCollection,
            Collections = { ActiveCollection, LeftoverCollection },
        };

        await CreateStore(gateway).ReplaceAllAsync(TwoDocuments);

        Assert.Equal(
            [$"delete {ActiveCollection}", $"delete {LeftoverCollection}"],
            gateway.Operations.Where(o => o.StartsWith("delete")));
    }

    [Fact]
    public async Task LeavesCollectionsOutsideTheNamingPatternUntouched()
    {
        var gateway = new FakeQdrantGateway
        {
            Collections =
            {
                "fonte-chunks",
                "fonte-chunks-backup",
                "fonte-chunks-2026",
                "fonte-chunks-202609251200000001",
                "outra-20260925120000000",
            },
        };

        await CreateStore(gateway).ReplaceAllAsync(TwoDocuments);

        Assert.DoesNotContain(gateway.Operations, o => o.StartsWith("delete"));
    }

    [Fact]
    public async Task CleanupFailureAfterSwitchReportsNewCollectionAsActive()
    {
        var failure = new InvalidOperationException("falha ao apagar");
        var gateway = new FakeQdrantGateway
        {
            AliasTarget = ActiveCollection,
            Collections = { ActiveCollection },
            FailDeleteOf = (ActiveCollection, failure),
        };

        var exception = await Assert.ThrowsAsync<VectorStoreCleanupException>(
            () => CreateStore(gateway).ReplaceAllAsync(TwoDocuments));

        Assert.Equal(NewCollection, exception.ActiveCollection);
        Assert.Equal(ActiveCollection, exception.FailedCollection);
        Assert.Same(failure, exception.InnerException);
        Assert.Equal(NewCollection, gateway.AliasTarget);
    }

    [Fact]
    public async Task EmptyChunkListPublishesEmptyCollection()
    {
        var gateway = new FakeQdrantGateway();

        await CreateStore(gateway).ReplaceAllAsync([]);

        Assert.Contains($"create {NewCollection} {Dimensions} Cosine", gateway.Operations);
        Assert.DoesNotContain(gateway.Operations, o => o.StartsWith("upsert"));
        Assert.Contains($"switch {Alias} {NewCollection} replace=False", gateway.Operations);
    }

    [Fact]
    public async Task PassesCancellationTokenToEveryOperation()
    {
        var gateway = new FakeQdrantGateway { AliasTarget = ActiveCollection, Collections = { ActiveCollection } };
        using var cancellation = new CancellationTokenSource();

        await CreateStore(gateway).ReplaceAllAsync(TwoDocuments, cancellation.Token);

        Assert.NotEmpty(gateway.Tokens);
        Assert.All(gateway.Tokens, token => Assert.Equal(cancellation.Token, token));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeQdrantGateway : IQdrantGateway
    {
        public string? AliasTarget { get; set; }

        public List<string> Collections { get; } = [];

        public (string DocumentPath, Exception Error)? FailUpsertOf { get; init; }

        public (string Collection, Exception Error)? FailDeleteOf { get; init; }

        public List<string> Operations { get; } = [];

        public List<IReadOnlyList<ChunkPoint>> Upserted { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public int IndexOf(string operation)
        {
            var index = Operations.IndexOf(operation);
            Assert.True(index >= 0, $"Operação não executada: {operation}");
            return index;
        }

        public int LastIndexOf(string prefix) => Operations.FindLastIndex(o => o.StartsWith(prefix));

        public Task<string?> GetAliasTargetAsync(string alias, CancellationToken cancellationToken)
        {
            Record($"get-alias {alias}", cancellationToken);
            return Task.FromResult(AliasTarget);
        }

        public Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken)
        {
            Record("list", cancellationToken);
            return Task.FromResult<IReadOnlyList<string>>([.. Collections]);
        }

        public Task CreateCollectionAsync(string name, int dimensions, VectorDistance distance, CancellationToken cancellationToken)
        {
            Record($"create {name} {dimensions} {distance}", cancellationToken);
            Collections.Add(name);
            return Task.CompletedTask;
        }

        public Task CreateKeywordIndexAsync(string collection, string field, CancellationToken cancellationToken)
        {
            Record($"index {collection} {field}", cancellationToken);
            return Task.CompletedTask;
        }

        public Task UpsertAsync(string collection, IReadOnlyList<ChunkPoint> points, CancellationToken cancellationToken)
        {
            Record($"upsert {collection} {string.Join(",", points.Select(p => p.DocumentPath))}", cancellationToken);

            if (FailUpsertOf is { } failure && points.Any(p => p.DocumentPath == failure.DocumentPath))
            {
                return Task.FromException(failure.Error);
            }

            Upserted.Add(points);
            return Task.CompletedTask;
        }

        public Task SwitchAliasAsync(string alias, string newCollection, bool replaceExisting, CancellationToken cancellationToken)
        {
            Record($"switch {alias} {newCollection} replace={replaceExisting}", cancellationToken);
            AliasTarget = newCollection;
            return Task.CompletedTask;
        }

        public Task DeleteCollectionAsync(string name, CancellationToken cancellationToken)
        {
            Record($"delete {name}", cancellationToken);

            if (FailDeleteOf is { } failure && failure.Collection == name)
            {
                return Task.FromException(failure.Error);
            }

            Collections.Remove(name);
            return Task.CompletedTask;
        }

        private void Record(string operation, CancellationToken cancellationToken)
        {
            Operations.Add(operation);
            Tokens.Add(cancellationToken);
        }
    }
}
