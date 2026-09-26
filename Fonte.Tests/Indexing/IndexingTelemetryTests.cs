using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Fonte.Api.Observability;
using Fonte.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace Fonte.Tests.Indexing;

/// <summary>
/// Verifica o que importa na telemetria própria: spans do pipeline, marcação de erro,
/// privacidade e métricas. Não verifica durações nem instrumentações automáticas.
/// </summary>
public sealed class IndexingTelemetryTests : IDisposable
{
    private const string FirstContent = "ActivitySource cria spans em aplicações .NET.";
    private const string SecondContent = "RAG recupera trechos antes de gerar a resposta.";

    private static readonly ActivitySource TestSource = new("Fonte.Tests.Indexing");

    private readonly DocumentIndexerHarness _harness = new();
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    public IndexingTelemetryTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is FonteTelemetry.Name || source == TestSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);

        _harness.WriteDocument("dotnet.md", FirstContent);
        _harness.WriteDocument(Path.Combine("guias", "rag.md"), SecondContent);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _harness.Dispose();
    }

    /// <summary>Executa a indexação sob um span pai próprio e devolve apenas os spans desse trace.</summary>
    private async Task<(Activity Parent, List<Activity> Spans)> IndexWithinParentAsync(bool expectFailure = false)
    {
        var parent = TestSource.StartActivity("teste")!;

        try
        {
            await _harness.CreateIndexer().IndexAsync();
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

    [Fact]
    public async Task EmitsPipelineSpansInOrderUnderSameParent()
    {
        var (parent, spans) = await IndexWithinParentAsync();

        Assert.Equal(
            ["documents.read", "documents.chunk", "embedding.create", "vectorstore.replace"],
            spans.Select(s => s.OperationName));
        Assert.All(spans, s => Assert.Equal(parent.SpanId, s.ParentSpanId));
        Assert.Equal(2, spans[0].GetTagItem("fonte.documents.count"));
        Assert.Equal(2, spans[1].GetTagItem("fonte.chunks.count"));
        Assert.Equal(true, spans[3].GetTagItem("fonte.cleanup.completed"));
    }

    [Fact]
    public async Task MarksFailedStageSpanAsError()
    {
        _harness.Generator = new FakeEmbeddingGenerator(DocumentIndexerHarness.Dimensions)
        {
            Failure = new HttpRequestException("falha no Gemini"),
        };

        var (_, spans) = await IndexWithinParentAsync(expectFailure: true);

        var embedding = Assert.Single(spans, s => s.OperationName == "embedding.create");
        Assert.Equal(ActivityStatusCode.Error, embedding.Status);
        Assert.Equal(typeof(HttpRequestException).FullName, embedding.GetTagItem("error.type"));
        Assert.DoesNotContain(spans, s => s.OperationName == "vectorstore.replace");
    }

    [Fact]
    public async Task SpanAttributesNeverContainContentOrDocumentPaths()
    {
        var (_, spans) = await IndexWithinParentAsync();

        var values = spans.SelectMany(s => s.TagObjects).Select(t => t.Value?.ToString() ?? "").ToList();
        string[] forbidden = [FirstContent, SecondContent, "dotnet.md", "guias/rag.md", "rag.md"];
        Assert.All(values, value => Assert.DoesNotContain(forbidden, f => value.Contains(f)));
    }

    [Fact]
    public async Task LogsNeverContainChunkContent()
    {
        await _harness.CreateIndexer().IndexAsync();
        _harness.Generator = new FakeEmbeddingGenerator(DocumentIndexerHarness.Dimensions) { Failure = new HttpRequestException("falha") };
        await Assert.ThrowsAsync<HttpRequestException>(() => _harness.CreateIndexer().IndexAsync());

        var texts = _harness.Logs.Entries
            .SelectMany(e => e.State.Values.Select(v => v?.ToString() ?? "").Append(e.Message))
            .ToList();
        Assert.NotEmpty(texts);
        Assert.All(texts, text =>
        {
            Assert.DoesNotContain(FirstContent, text);
            Assert.DoesNotContain(SecondContent, text);
        });
    }

    [Fact]
    public async Task ConfigurationFailureIsLoggedAsIndexingFailureWithStage()
    {
        _harness.EmbedderFactory = () => throw new InvalidOperationException("A chave da Gemini API não está configurada em 'Gemini:ApiKey'.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.CreateIndexer().IndexAsync());

        var error = Assert.Single(_harness.Logs.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal("configuration", error.State["Stage"]);
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    [Fact]
    public async Task RecordsMetricsForPublishedAndFailedAttempts()
    {
        var measurements = new ConcurrentQueue<(string Instrument, double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Scope == _harness.MeterFactory)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Enqueue((instrument.Name, value, ToDictionary(tags))));
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Enqueue((instrument.Name, value, ToDictionary(tags))));
        meterListener.Start();

        await _harness.CreateIndexer().IndexAsync();
        _harness.Generator = new FakeEmbeddingGenerator(DocumentIndexerHarness.Dimensions) { Failure = new HttpRequestException("falha") };
        await Assert.ThrowsAsync<HttpRequestException>(() => _harness.CreateIndexer().IndexAsync());

        Assert.Equal(2, measurements.Where(m => m.Instrument == "fonte.indexing.documents").Sum(m => m.Value));
        Assert.Equal(2, measurements.Where(m => m.Instrument == "fonte.indexing.chunks").Sum(m => m.Value));

        var attempts = measurements.Where(m => m.Instrument == "fonte.indexing.duration").ToList();
        Assert.Equal(["published", "failed"], attempts.Select(a => a.Tags["fonte.indexing.outcome"]));
        Assert.Equal(typeof(HttpRequestException).FullName, attempts[1].Tags["error.type"]);
        Assert.False(attempts[0].Tags.ContainsKey("error.type"));
    }

    private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, object?>();
        foreach (var tag in tags)
        {
            result[tag.Key] = tag.Value;
        }

        return result;
    }
}
