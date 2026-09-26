using Fonte.Api.Embeddings;
using Fonte.Api.Indexing;
using Fonte.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace Fonte.Tests.Embeddings;

public class ChunkEmbedderTests
{
    private const int Dimensions = 128;

    private static ChunkEmbedder CreateEmbedder(FakeEmbeddingGenerator generator) =>
        new(generator, Options.Create(new GeminiOptions { EmbeddingDimensions = Dimensions }));

    [Fact]
    public async Task SendsDocumentPathAsTitleAndChunkContentAsText()
    {
        var generator = new FakeEmbeddingGenerator(Dimensions);
        var chunk = new DocumentChunk("guias/observabilidade.md", 0, "ActivitySource cria spans.");

        await CreateEmbedder(generator).EmbedAsync([chunk]);

        var call = Assert.Single(generator.Calls);
        Assert.Equal(["title: guias/observabilidade.md | text: ActivitySource cria spans."], call);
    }

    [Fact]
    public async Task MakesOneCallPerChunkWithSingleTextInChunkOrder()
    {
        var generator = new FakeEmbeddingGenerator(Dimensions);
        DocumentChunk[] chunks =
        [
            new("a.md", 0, "um"),
            new("a.md", 1, "dois"),
            new("b.md", 0, "tres"),
        ];

        await CreateEmbedder(generator).EmbedAsync(chunks);

        Assert.Equal(
            [
                ["title: a.md | text: um"],
                ["title: a.md | text: dois"],
                ["title: b.md | text: tres"],
            ],
            generator.Calls);
    }

    [Fact]
    public async Task AssociatesEachVectorWithItsChunk()
    {
        var generator = new FakeEmbeddingGenerator(Dimensions);
        DocumentChunk[] chunks = [new("a.md", 0, "um"), new("a.md", 1, "dois")];

        var embedded = await CreateEmbedder(generator).EmbedAsync(chunks);

        Assert.Equal(chunks, embedded.Select(e => e.Chunk));
        Assert.Equal(
            [FakeEmbeddingGenerator.VectorFor(0, Dimensions), FakeEmbeddingGenerator.VectorFor(1, Dimensions)],
            embedded.Select(e => e.Vector.ToArray()));
    }

    [Fact]
    public async Task EmptyListDoesNotCallGenerator()
    {
        var generator = new FakeEmbeddingGenerator(Dimensions);

        var embedded = await CreateEmbedder(generator).EmbedAsync([]);

        Assert.Empty(embedded);
        Assert.Empty(generator.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ThrowsWhenGeneratorDoesNotReturnExactlyOneEmbedding(int embeddingsReturned)
    {
        var generator = new FakeEmbeddingGenerator(Dimensions) { EmbeddingsPerCall = embeddingsReturned };
        var embedder = CreateEmbedder(generator);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => embedder.EmbedAsync([new DocumentChunk("a.md", 0, "um")]));
    }

    [Fact]
    public async Task ThrowsWhenVectorHasUnexpectedDimensions()
    {
        var generator = new FakeEmbeddingGenerator(Dimensions + 1);
        var embedder = CreateEmbedder(generator);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => embedder.EmbedAsync([new DocumentChunk("a.md", 0, "um")]));
    }

    [Fact]
    public async Task PassesCancellationTokenToGenerator()
    {
        var generator = new FakeEmbeddingGenerator(Dimensions);
        using var cancellation = new CancellationTokenSource();

        await CreateEmbedder(generator).EmbedAsync([new DocumentChunk("a.md", 0, "um")], cancellation.Token);

        Assert.Equal(cancellation.Token, Assert.Single(generator.CancellationTokens));
    }
}
