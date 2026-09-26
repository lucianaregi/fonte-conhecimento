using Fonte.Api.Embeddings;
using Fonte.Api.Indexing;
using Microsoft.Extensions.AI;
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

    private sealed class FakeEmbeddingGenerator(int dimensions) : IEmbeddingGenerator<string, Embedding<float>>
    {
        private int _generated;

        public int EmbeddingsPerCall { get; init; } = 1;

        public List<string[]> Calls { get; } = [];

        public List<CancellationToken> CancellationTokens { get; } = [];

        public static float[] VectorFor(int sequence, int dimensions) =>
            Enumerable.Repeat((float)sequence, dimensions).ToArray();

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(values.ToArray());
            CancellationTokens.Add(cancellationToken);

            var embeddings = new GeneratedEmbeddings<Embedding<float>>();
            for (var i = 0; i < EmbeddingsPerCall; i++)
            {
                embeddings.Add(new Embedding<float>(VectorFor(_generated++, dimensions)));
            }

            return Task.FromResult(embeddings);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
