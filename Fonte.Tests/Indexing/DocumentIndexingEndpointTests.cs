using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fonte.Api.VectorStore;
using Fonte.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fonte.Tests.Indexing;

public sealed class DocumentIndexingEndpointTests(FonteApiFactory factory)
    : IClassFixture<FonteApiFactory>, IDisposable
{
    private const int Dimensions = 128;
    private const string ActiveCollection = "fonte-chunks-20260920100000000";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fonte-tests", Guid.NewGuid().ToString("N"));
    private readonly CapturingLoggerProvider _logs = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteDocument(string name, string content)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, name), content);
    }

    /// <param name="generator">Quando nulo, o Gemini real fica registrado (sem chave).</param>
    /// <param name="gateway">Quando nulo, o Qdrant real fica registrado (sem URL).</param>
    private WebApplicationFactory<Program> CreateApp(FakeEmbeddingGenerator? generator, FakeQdrantGateway? gateway) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Documents:Path", _root);
            builder.UseSetting("Gemini:EmbeddingDimensions", Dimensions.ToString());
            builder.UseSetting("Gemini:ApiKey", "");
            builder.UseSetting("Qdrant:Url", "");
            builder.UseSetting("Qdrant:ApiKey", "");
            builder.ConfigureLogging(logging => logging.AddProvider(_logs));
            builder.ConfigureTestServices(services =>
            {
                if (generator is not null)
                {
                    services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(generator);
                }

                if (gateway is not null)
                {
                    services.AddSingleton<IQdrantGateway>(gateway);
                }
            });
        });

    [Fact]
    public async Task ReturnsOkWithDocumentsChunksAndCleanupCompleted()
    {
        WriteDocument("rag.md", "# RAG");
        WriteDocument("dotnet.md", "# .NET");
        using var app = CreateApp(new FakeEmbeddingGenerator(Dimensions), new FakeQdrantGateway());

        var response = await app.CreateClient().PostAsync("/documents/index", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            ["documents", "chunks", "cleanupCompleted"],
            body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(2, body.GetProperty("documents").GetInt32());
        Assert.Equal(2, body.GetProperty("chunks").GetInt32());
        Assert.True(body.GetProperty("cleanupCompleted").GetBoolean());
    }

    [Fact]
    public async Task ReturnsOkWithCleanupNotCompletedWhenCleanupFailsAfterPublishing()
    {
        WriteDocument("rag.md", "# RAG");
        var gateway = new FakeQdrantGateway
        {
            AliasTarget = ActiveCollection,
            Collections = { ActiveCollection },
            FailDeleteOf = (ActiveCollection, new InvalidOperationException("falha ao apagar")),
        };
        using var app = CreateApp(new FakeEmbeddingGenerator(Dimensions), gateway);

        var response = await app.CreateClient().PostAsync("/documents/index", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("cleanupCompleted").GetBoolean());
    }

    [Fact]
    public async Task ReturnsConflictWhileAnotherIndexingIsRunning()
    {
        WriteDocument("rag.md", "# RAG");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generator = new FakeEmbeddingGenerator(Dimensions) { Gate = gate };
        using var app = CreateApp(generator, new FakeQdrantGateway());
        var client = app.CreateClient();

        var first = client.PostAsync("/documents/index", null);
        await generator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await client.PostAsync("/documents/index", null).WaitAsync(TimeSpan.FromSeconds(10));
        gate.SetResult();

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
    }

    [Fact]
    public async Task ReturnsUnprocessableEntityForEmptyFolderWithoutTouchingIndex()
    {
        Directory.CreateDirectory(_root);
        var gateway = new FakeQdrantGateway { AliasTarget = ActiveCollection };
        using var app = CreateApp(new FakeEmbeddingGenerator(Dimensions), gateway);

        var response = await app.CreateClient().PostAsync("/documents/index", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(gateway.Operations);
    }

    [Fact]
    public async Task ReturnsServerErrorAndKeepsActiveIndexWhenGeminiFails()
    {
        WriteDocument("rag.md", "# RAG");
        var gateway = new FakeQdrantGateway { AliasTarget = ActiveCollection };
        var generator = new FakeEmbeddingGenerator(Dimensions) { Failure = new HttpRequestException("falha no Gemini") };
        using var app = CreateApp(generator, gateway);

        var response = await app.CreateClient().PostAsync("/documents/index", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ActiveCollection, gateway.AliasTarget);
        Assert.DoesNotContain(gateway.Operations, o => o.StartsWith("switch"));
    }

    [Fact]
    public async Task MissingGeminiKeyIsRecordedAsIndexingFailureBeforeAnyExternalCall()
    {
        WriteDocument("rag.md", "# RAG");
        var gateway = new FakeQdrantGateway();
        using var app = CreateApp(generator: null, gateway);
        using var failures = ListenForFailedAttempts(app);
        var client = app.CreateClient();

        var response = await client.PostAsync("/documents/index", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(gateway.Operations);
        AssertConfigurationFailureLogged("Gemini:ApiKey");
        Assert.Equal(1, failures.Count);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task MissingQdrantUrlIsRecordedAsIndexingFailureBeforeAnyExternalCall()
    {
        WriteDocument("rag.md", "# RAG");
        var generator = new FakeEmbeddingGenerator(Dimensions);
        using var app = CreateApp(generator, gateway: null);
        using var failures = ListenForFailedAttempts(app);

        var response = await app.CreateClient().PostAsync("/documents/index", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(generator.Calls);
        AssertConfigurationFailureLogged("Qdrant:Url");
        Assert.Equal(1, failures.Count);
    }

    private void AssertConfigurationFailureLogged(string missingKey)
    {
        var error = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Error && e.State.ContainsKey("Stage"));
        Assert.Equal("configuration", error.State["Stage"]);
        Assert.Contains(missingKey, error.Exception?.Message);
    }

    private static FailedAttemptCounter ListenForFailedAttempts(WebApplicationFactory<Program> app) =>
        new(app.Services.GetRequiredService<IMeterFactory>());

    /// <summary>Conta tentativas com <c>outcome = failed</c> no <see cref="IMeterFactory"/> da aplicação de teste.</summary>
    private sealed class FailedAttemptCounter : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<string?> _outcomes = new();

        public FailedAttemptCounter(IMeterFactory meterFactory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Scope == meterFactory && instrument.Name == "fonte.indexing.duration")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == "fonte.indexing.outcome")
                    {
                        _outcomes.Enqueue(tag.Value as string);
                    }
                }
            });
            _listener.Start();
        }

        public int Count => _outcomes.Count(o => o == "failed");

        public void Dispose() => _listener.Dispose();
    }
}
