using Fonte.Api.Embeddings;
using Fonte.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace Fonte.Tests.Embeddings;

public class QueryEmbedderTests
{
    private const int Dimensions = 128;

    private static QueryEmbedder CreateEmbedder(FakeEmbeddingGenerator generator) =>
        new(generator, Options.Create(new GeminiOptions { EmbeddingDimensions = Dimensions }));

    [Fact]
    public async Task SendsQuestionInSearchResultQueryFormat()
    {
        var generator = new FakeEmbeddingGenerator(Dimensions);

        await CreateEmbedder(generator).EmbedAsync("Qual é a função do ActivitySource?");

        var call = Assert.Single(generator.Calls);
        Assert.Equal(["task: search result | query: Qual é a função do ActivitySource?"], call);
    }

    [Fact]
    public async Task ReturnsGeneratedVector()
    {
        var generator = new FakeEmbeddingGenerator(Dimensions);

        var vector = await CreateEmbedder(generator).EmbedAsync("pergunta");

        Assert.Equal(FakeEmbeddingGenerator.VectorFor(0, Dimensions), vector.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ThrowsWhenGeneratorDoesNotReturnExactlyOneEmbedding(int embeddingsReturned)
    {
        var generator = new FakeEmbeddingGenerator(Dimensions) { EmbeddingsPerCall = embeddingsReturned };

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateEmbedder(generator).EmbedAsync("pergunta"));
    }

    [Fact]
    public async Task ThrowsWhenVectorHasUnexpectedDimensions()
    {
        var generator = new FakeEmbeddingGenerator(Dimensions + 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateEmbedder(generator).EmbedAsync("pergunta"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyQuestionThrowsWithoutCallingGenerator(string question)
    {
        var generator = new FakeEmbeddingGenerator(Dimensions);

        await Assert.ThrowsAsync<ArgumentException>(() => CreateEmbedder(generator).EmbedAsync(question));

        Assert.Empty(generator.Calls);
    }

    [Fact]
    public async Task PassesCancellationTokenToGenerator()
    {
        var generator = new FakeEmbeddingGenerator(Dimensions);
        using var cancellation = new CancellationTokenSource();

        await CreateEmbedder(generator).EmbedAsync("pergunta", cancellation.Token);

        Assert.Equal(cancellation.Token, Assert.Single(generator.CancellationTokens));
    }
}
