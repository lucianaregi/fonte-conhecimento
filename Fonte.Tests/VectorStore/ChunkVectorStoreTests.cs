using System.Globalization;
using Fonte.Api.Embeddings;
using Fonte.Api.Indexing;
using Fonte.Api.Retrieval;
using Fonte.Api.VectorStore;
using Fonte.Tests.Fakes;
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
    public async Task SearchQueriesActiveIndexThroughAliasWithRequestedLimit()
    {
        var gateway = new FakeQdrantGateway();
        var stored = new RetrievedChunk("rag.md", 2, "A recuperação encontra trechos.", 0.8f);
        gateway.SearchResults.Add(stored);
        float[] vector = [0.1f, 0.2f];
        using var cancellation = new CancellationTokenSource();

        var results = await CreateStore(gateway).SearchAsync(vector, limit: 3, cancellation.Token);

        Assert.Equal([$"search {Alias} 3"], gateway.Operations);
        Assert.Equal(vector, gateway.LastSearchVector!.Value.ToArray());
        Assert.Equal(cancellation.Token, Assert.Single(gateway.Tokens));
        Assert.Equal([stored], results);
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
}
