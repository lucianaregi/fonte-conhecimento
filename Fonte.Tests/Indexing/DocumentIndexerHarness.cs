using System.Diagnostics.Metrics;
using Fonte.Api.Embeddings;
using Fonte.Api.Indexing;
using Fonte.Api.Observability;
using Fonte.Api.VectorStore;
using Fonte.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fonte.Tests.Indexing;

/// <summary>
/// Monta um <see cref="DocumentIndexer"/> com os componentes reais e fakes apenas nas bordas
/// (Gemini e Qdrant), sobre uma pasta temporária isolada.
/// </summary>
public sealed class DocumentIndexerHarness : IDisposable
{
    public const int Dimensions = 128;

    private readonly ServiceProvider _metricsServices = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly ILoggerFactory _loggerFactory;

    public DocumentIndexerHarness()
    {
        Directory.CreateDirectory(Root);
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(Logs));
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "fonte-tests", Guid.NewGuid().ToString("N"));

    public string DocumentsPath { get; set; } = "documents";

    public FakeEmbeddingGenerator Generator { get; set; } = new(Dimensions);

    public FakeQdrantGateway Gateway { get; set; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    public IMeterFactory MeterFactory => _metricsServices.GetRequiredService<IMeterFactory>();

    /// <summary>Substitui a resolução do <see cref="ChunkEmbedder"/> (ex.: simular configuração ausente).</summary>
    public Func<ChunkEmbedder>? EmbedderFactory { get; set; }

    /// <summary>Substitui a resolução do <see cref="ChunkVectorStore"/> (ex.: simular configuração ausente).</summary>
    public Func<ChunkVectorStore>? VectorStoreFactory { get; set; }

    public void WriteDocument(string relativePath, string content)
    {
        var fullPath = Path.Combine(Root, DocumentsPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    public void CreateEmptyDocumentsFolder() => Directory.CreateDirectory(Path.Combine(Root, DocumentsPath));

    public DocumentIndexer CreateIndexer()
    {
        var geminiOptions = Options.Create(new GeminiOptions { EmbeddingDimensions = Dimensions });

        return new DocumentIndexer(
            new MarkdownDocumentReader(Options.Create(new DocumentsOptions { Path = DocumentsPath }), new StubHostEnvironment(Root)),
            new MarkdownChunker(Options.Create(new DocumentsOptions())),
            EmbedderFactory ?? (() => new ChunkEmbedder(Generator, geminiOptions)),
            VectorStoreFactory ?? (() => new ChunkVectorStore(
                Gateway,
                Options.Create(new QdrantOptions()),
                geminiOptions,
                TimeProvider.System)),
            new IndexingMetrics(MeterFactory),
            TimeProvider.System,
            _loggerFactory.CreateLogger<DocumentIndexer>());
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _metricsServices.Dispose();
        Directory.Delete(Root, recursive: true);
    }
}
