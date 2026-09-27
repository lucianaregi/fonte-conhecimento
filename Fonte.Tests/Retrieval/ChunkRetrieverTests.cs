using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Fonte.Api.Embeddings;
using Fonte.Api.Observability;
using Fonte.Api.Retrieval;
using Fonte.Api.VectorStore;
using Fonte.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fonte.Tests.Retrieval;

public sealed class ChunkRetrieverTests : IDisposable
{
    private const int Dimensions = 128;
    private const string Alias = "fonte-chunks";
    private const string Question = "Qual é a função do ActivitySource em uma aplicação .NET?";
    private const string SecretContent = "ActivitySource cria spans em aplicações .NET.";

    private static readonly ActivitySource TestSource = new("Fonte.Tests.Retrieval");

    private readonly ServiceProvider _metricsServices = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly CapturingLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    private static readonly RetrievedChunk[] StoredResults =
    [
        new("observabilidade.md", 4, SecretContent, 0.91f),
        new("dotnet.md", 5, "O ASP.NET Core hospeda a API.", 0.74f),
        new("rag.md", 3, "A recuperação encontra trechos.", 0.62f),
        new("rag.md", 1, "Por que usar RAG.", 0.55f),
    ];

    public ChunkRetrieverTests()
    {
        _loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(_logs));
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is FonteTelemetry.Name || source == TestSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _loggerFactory.Dispose();
        _metricsServices.Dispose();
    }

    private FakeEmbeddingGenerator Generator { get; set; } = new(Dimensions);

    private FakeQdrantGateway Gateway { get; set; } = CreateGatewayWith(StoredResults);

    private Func<QueryEmbedder>? EmbedderFactory { get; set; }

    private Func<ChunkVectorStore>? VectorStoreFactory { get; set; }

    private IMeterFactory MeterFactory => _metricsServices.GetRequiredService<IMeterFactory>();

    private static FakeQdrantGateway CreateGatewayWith(IEnumerable<RetrievedChunk> results)
    {
        var gateway = new FakeQdrantGateway();
        gateway.SearchResults.AddRange(results);
        return gateway;
    }

    private ChunkRetriever CreateRetriever(int topK = 3)
    {
        var geminiOptions = Options.Create(new GeminiOptions { EmbeddingDimensions = Dimensions });

        return new ChunkRetriever(
            EmbedderFactory ?? (() => new QueryEmbedder(Generator, geminiOptions)),
            VectorStoreFactory ?? (() => new ChunkVectorStore(Gateway, Options.Create(new QdrantOptions { CollectionName = Alias }), geminiOptions, TimeProvider.System)),
            Options.Create(new RetrievalOptions { TopK = topK }),
            new RetrievalMetrics(MeterFactory),
            TimeProvider.System,
            _loggerFactory.CreateLogger<ChunkRetriever>());
    }

    [Fact]
    public async Task SearchesAliasWithConfiguredTopKAndQuestionVector()
    {
        var results = await CreateRetriever(topK: 3).RetrieveAsync(Question);

        Assert.Equal([$"search {Alias} 3"], Gateway.Operations);
        Assert.Equal(FakeEmbeddingGenerator.VectorFor(0, Dimensions), Gateway.LastSearchVector!.Value.ToArray());
        Assert.Equal(StoredResults.Take(3), results);
    }

    [Fact]
    public async Task KeepsOrderReturnedByVectorStore()
    {
        var results = await CreateRetriever(topK: 4).RetrieveAsync(Question);

        Assert.Equal(StoredResults, results);
    }

    [Fact]
    public async Task ExistingIndexWithoutResultsReturnsEmptyList()
    {
        Gateway = CreateGatewayWith([]);

        var results = await CreateRetriever().RetrieveAsync(Question);

        Assert.Empty(results);
        Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task MissingAliasPropagatesAndIsLoggedAsSearchFailure()
    {
        var failure = new InvalidOperationException("Collection `fonte-chunks` doesn't exist!");
        Gateway = new FakeQdrantGateway { SearchFailure = failure };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateRetriever().RetrieveAsync(Question));

        Assert.Same(failure, exception);
        var error = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal("search", error.State["Stage"]);
    }

    [Fact]
    public async Task EmbeddingFailurePropagatesWithoutSearching()
    {
        var failure = new HttpRequestException("falha no Gemini");
        Generator = new FakeEmbeddingGenerator(Dimensions) { Failure = failure };

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => CreateRetriever().RetrieveAsync(Question));

        Assert.Same(failure, exception);
        Assert.Empty(Gateway.Operations);
        Assert.Equal("embedding", Assert.Single(_logs.Entries, e => e.Level == LogLevel.Error).State["Stage"]);
    }

    [Fact]
    public async Task ConfigurationFailureIsLoggedWithoutCallingServices()
    {
        EmbedderFactory = () => throw new InvalidOperationException("A chave da Gemini API não está configurada em 'Gemini:ApiKey'.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateRetriever().RetrieveAsync(Question));

        Assert.Empty(Generator.Calls);
        Assert.Empty(Gateway.Operations);
        Assert.Equal("configuration", Assert.Single(_logs.Entries, e => e.Level == LogLevel.Error).State["Stage"]);
    }

    [Fact]
    public async Task MissingQdrantConfigurationFailsBeforeCallingGemini()
    {
        VectorStoreFactory = () => throw new InvalidOperationException("O endereço do Qdrant não está configurado em 'Qdrant:Url'.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateRetriever().RetrieveAsync(Question));

        Assert.Empty(Generator.Calls);
        Assert.Equal("configuration", Assert.Single(_logs.Entries, e => e.Level == LogLevel.Error).State["Stage"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyQuestionThrowsBeforeAnyWork(string question)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CreateRetriever().RetrieveAsync(question));

        Assert.Empty(Generator.Calls);
        Assert.Empty(Gateway.Operations);
    }

    [Fact]
    public async Task PassesCancellationTokenToExternalServices()
    {
        using var cancellation = new CancellationTokenSource();

        await CreateRetriever().RetrieveAsync(Question, cancellation.Token);

        Assert.Equal(cancellation.Token, Assert.Single(Generator.CancellationTokens));
        Assert.Equal(cancellation.Token, Assert.Single(Gateway.Tokens));
    }

    [Fact]
    public async Task EmitsEmbeddingAndSearchSpansUnderSameParent()
    {
        var (parent, spans) = await RetrieveWithinParentAsync();

        Assert.Equal(["embedding.create", "retrieval.search"], spans.Select(s => s.OperationName));
        Assert.All(spans, s => Assert.Equal(parent.SpanId, s.ParentSpanId));
        Assert.Equal(3, spans[1].GetTagItem("fonte.retrieval.top_k"));
        Assert.Equal(3, spans[1].GetTagItem("fonte.retrieval.results"));
    }

    [Fact]
    public async Task MarksFailedSearchSpanAsError()
    {
        Gateway = new FakeQdrantGateway { SearchFailure = new InvalidOperationException("alias inexistente") };

        var (_, spans) = await RetrieveWithinParentAsync(expectFailure: true);

        var search = Assert.Single(spans, s => s.OperationName == "retrieval.search");
        Assert.Equal(ActivityStatusCode.Error, search.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, search.GetTagItem("error.type"));
    }

    [Fact]
    public async Task TelemetryNeverContainsQuestionContentOrDocumentPaths()
    {
        var (_, spans) = await RetrieveWithinParentAsync();
        Gateway = new FakeQdrantGateway { SearchFailure = new InvalidOperationException("falha") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateRetriever().RetrieveAsync(Question));

        string[] forbidden = [Question, SecretContent, "observabilidade.md", "dotnet.md", "rag.md"];
        var spanValues = spans.SelectMany(s => s.TagObjects).Select(t => t.Value?.ToString() ?? "");
        var logValues = _logs.Entries.SelectMany(e => e.State.Values.Select(v => v?.ToString() ?? "").Append(e.Message));
        Assert.All(spanValues.Concat(logValues), value => Assert.DoesNotContain(forbidden, f => value.Contains(f)));
        Assert.NotEmpty(_logs.Entries);
    }

    [Fact]
    public async Task RecordsDurationWithOutcome()
    {
        var outcomes = new ConcurrentQueue<Dictionary<string, object?>>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Scope == MeterFactory && instrument.Name == "fonte.retrieval.duration")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            var values = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                values[tag.Key] = tag.Value;
            }

            outcomes.Enqueue(values);
        });
        meterListener.Start();

        await CreateRetriever().RetrieveAsync(Question);
        Gateway = new FakeQdrantGateway { SearchFailure = new InvalidOperationException("falha") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateRetriever().RetrieveAsync(Question));

        var recorded = outcomes.ToList();
        Assert.Equal(["success", "failed"], recorded.Select(r => r["fonte.retrieval.outcome"]));
        Assert.False(recorded[0].ContainsKey("error.type"));
        Assert.Equal(typeof(InvalidOperationException).FullName, recorded[1]["error.type"]);
    }

    private async Task<(Activity Parent, List<Activity> Spans)> RetrieveWithinParentAsync(bool expectFailure = false)
    {
        var parent = TestSource.StartActivity("teste")!;

        try
        {
            await CreateRetriever().RetrieveAsync(Question);
        }
        catch when (expectFailure)
        {
        }
        finally
        {
            parent.Stop();
        }

        var spans = _stopped
            .Where(a => a.TraceId == parent.TraceId && a.Source.Name == FonteTelemetry.Name)
            .OrderBy(a => a.StartTimeUtc)
            .ToList();

        return (parent, spans);
    }
}
