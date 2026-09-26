using Fonte.Api.Embeddings;
using Fonte.Api.Indexing;
using Fonte.Api.VectorStore;
using Fonte.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace Fonte.Tests.Indexing;

public sealed class DocumentIndexerTests : IDisposable
{
    private const string ActiveCollection = "fonte-chunks-20260920100000000";

    private readonly DocumentIndexerHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private void WriteThreeDocuments()
    {
        _harness.WriteDocument("rag.md", "# RAG");
        _harness.WriteDocument("dotnet.md", "# .NET");
        _harness.WriteDocument(Path.Combine("guias", "observabilidade.md"), "# Observabilidade");
    }

    [Fact]
    public async Task PublishesAllDocumentsAndReturnsCounts()
    {
        WriteThreeDocuments();

        var outcome = await _harness.CreateIndexer().IndexAsync();

        Assert.Equal(DocumentIndexingOutcome.Published(documents: 3, chunks: 3, cleanupCompleted: true), outcome);
        Assert.Equal(3, _harness.Generator.Calls.Count);
        Assert.Contains(_harness.Gateway.Operations, o => o.StartsWith("switch"));
    }

    [Fact]
    public async Task EmptyFolderReturnsNoDocumentsWithoutTouchingIndex()
    {
        _harness.CreateEmptyDocumentsFolder();

        var outcome = await _harness.CreateIndexer().IndexAsync();

        Assert.Equal(DocumentIndexingOutcome.NoDocuments, outcome);
        Assert.Empty(_harness.Generator.Calls);
        Assert.Empty(_harness.Gateway.Operations);
    }

    [Fact]
    public async Task EmbeddingFailurePropagatesWithoutSwitchingAlias()
    {
        WriteThreeDocuments();
        var failure = new HttpRequestException("falha no Gemini");
        _harness.Generator = new FakeEmbeddingGenerator(DocumentIndexerHarness.Dimensions) { Failure = failure };
        _harness.Gateway.AliasTarget = ActiveCollection;

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _harness.CreateIndexer().IndexAsync());

        Assert.Same(failure, exception);
        Assert.Empty(_harness.Gateway.Operations);
        Assert.Equal(ActiveCollection, _harness.Gateway.AliasTarget);
    }

    [Fact]
    public async Task VectorStoreFailurePropagatesWithoutSwitchingAlias()
    {
        WriteThreeDocuments();
        var failure = new InvalidOperationException("falha no Qdrant");
        _harness.Gateway = new FakeQdrantGateway { AliasTarget = ActiveCollection, FailUpsertOf = ("rag.md", failure) };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.CreateIndexer().IndexAsync());

        Assert.Same(failure, exception);
        Assert.DoesNotContain(_harness.Gateway.Operations, o => o.StartsWith("switch"));
        Assert.Equal(ActiveCollection, _harness.Gateway.AliasTarget);
    }

    [Fact]
    public async Task CleanupFailureReturnsPublishedWithCleanupNotCompleted()
    {
        WriteThreeDocuments();
        _harness.Gateway = new FakeQdrantGateway
        {
            AliasTarget = ActiveCollection,
            Collections = { ActiveCollection },
            FailDeleteOf = (ActiveCollection, new InvalidOperationException("falha ao apagar")),
        };

        var outcome = await _harness.CreateIndexer().IndexAsync();

        Assert.Equal(DocumentIndexingOutcome.Published(documents: 3, chunks: 3, cleanupCompleted: false), outcome);
        Assert.NotEqual(ActiveCollection, _harness.Gateway.AliasTarget);
    }

    [Fact]
    public async Task ConcurrentCallReturnsAlreadyRunning()
    {
        WriteThreeDocuments();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _harness.Generator = new FakeEmbeddingGenerator(DocumentIndexerHarness.Dimensions) { Gate = gate };
        var indexer = _harness.CreateIndexer();

        var first = indexer.IndexAsync();
        await _harness.Generator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await indexer.IndexAsync().WaitAsync(TimeSpan.FromSeconds(10));
        gate.SetResult();

        Assert.Equal(DocumentIndexingOutcome.AlreadyRunning, second);
        Assert.Equal(DocumentIndexingStatus.Published, (await first).Status);
    }

    [Fact]
    public async Task ReleasesLockAndAllowsRetryAfterConfigurationFailure()
    {
        WriteThreeDocuments();
        var attempts = 0;
        var geminiOptions = Options.Create(new GeminiOptions { EmbeddingDimensions = DocumentIndexerHarness.Dimensions });
        _harness.EmbedderFactory = () => ++attempts == 1
            ? throw new InvalidOperationException("A chave da Gemini API não está configurada em 'Gemini:ApiKey'.")
            : new ChunkEmbedder(_harness.Generator, geminiOptions);
        var indexer = _harness.CreateIndexer();

        await Assert.ThrowsAsync<InvalidOperationException>(() => indexer.IndexAsync());
        var outcome = await indexer.IndexAsync();

        Assert.Equal(DocumentIndexingStatus.Published, outcome.Status);
    }

    [Fact]
    public async Task ReleasesLockAfterSuccess()
    {
        WriteThreeDocuments();
        var indexer = _harness.CreateIndexer();

        await indexer.IndexAsync();
        var outcome = await indexer.IndexAsync();

        Assert.Equal(DocumentIndexingStatus.Published, outcome.Status);
    }

    [Fact]
    public async Task MissingGeminiConfigurationFailsBeforeReadingOrCallingServices()
    {
        // A pasta não existe: se a leitura acontecesse antes, a falha seria DirectoryNotFoundException.
        _harness.DocumentsPath = "inexistente";
        var failure = new InvalidOperationException("A chave da Gemini API não está configurada em 'Gemini:ApiKey'.");
        _harness.EmbedderFactory = () => throw failure;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.CreateIndexer().IndexAsync());

        Assert.Same(failure, exception);
        Assert.Empty(_harness.Generator.Calls);
        Assert.Empty(_harness.Gateway.Operations);
    }

    [Fact]
    public async Task MissingQdrantConfigurationFailsBeforeReadingOrCallingServices()
    {
        _harness.DocumentsPath = "inexistente";
        var failure = new InvalidOperationException("O endereço do Qdrant não está configurado em 'Qdrant:Url'.");
        _harness.VectorStoreFactory = () => throw failure;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.CreateIndexer().IndexAsync());

        Assert.Same(failure, exception);
        Assert.Empty(_harness.Generator.Calls);
        Assert.Empty(_harness.Gateway.Operations);
    }

    [Fact]
    public async Task PassesCancellationTokenToExternalServices()
    {
        WriteThreeDocuments();
        using var cancellation = new CancellationTokenSource();

        await _harness.CreateIndexer().IndexAsync(cancellation.Token);

        Assert.All(_harness.Generator.CancellationTokens, token => Assert.Equal(cancellation.Token, token));
        Assert.All(_harness.Gateway.Tokens, token => Assert.Equal(cancellation.Token, token));
        Assert.NotEmpty(_harness.Gateway.Tokens);
    }
}
