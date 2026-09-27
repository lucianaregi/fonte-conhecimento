using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fonte.Api.Answering;
using Fonte.Api.Retrieval;
using Fonte.Api.VectorStore;
using Fonte.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fonte.Tests.Answering;

public sealed class QuestionEndpointTests(FonteApiFactory factory)
    : IClassFixture<FonteApiFactory>, IDisposable
{
    private const int Dimensions = 128;
    private const string Question = "Qual é a função do ActivitySource?";
    private const string ModelAnswer = "O ActivitySource cria spans para traces próprios.";

    private static readonly RetrievedChunk[] Stored =
    [
        new("observabilidade.md", 3, "O `ActivitySource` é o ponto de partida para criar traces.", 0.7694f),
        new("dotnet.md", 3, "A configuração de uma aplicação ASP.NET Core.", 0.6479f),
        new("observabilidade.md", 2, "O OpenTelemetry padroniza traces, métricas e logs.", 0.6467f),
        new("rag.md", 1, "Antes de responder perguntas, os documentos são preparados.", 0.5f),
    ];

    private readonly CapturingLoggerProvider _logs = new();
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private ActivityListener? _listener;

    public void Dispose() => _listener?.Dispose();

    private FakeEmbeddingGenerator Generator { get; set; } = new(Dimensions);

    private FakeQdrantGateway Gateway { get; set; } = GatewayWith(Stored);

    private FakeChatClient ChatClient { get; set; } = new() { Response = FakeChatClient.Json("answered", ModelAnswer) };

    private static FakeQdrantGateway GatewayWith(IEnumerable<RetrievedChunk> results)
    {
        var gateway = new FakeQdrantGateway();
        gateway.SearchResults.AddRange(results);
        return gateway;
    }

    /// <param name="withGemini">Quando falso, o Gemini real fica registrado (sem chave).</param>
    private WebApplicationFactory<Program> CreateApp(bool withGemini = true) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Gemini:EmbeddingDimensions", Dimensions.ToString());
            builder.UseSetting("Gemini:ApiKey", "");
            builder.UseSetting("Qdrant:Url", "");
            builder.UseSetting("Qdrant:ApiKey", "");
            builder.UseSetting("Retrieval:TopK", "3");
            builder.ConfigureLogging(logging => logging.AddProvider(_logs));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IQdrantGateway>(Gateway);

                if (withGemini)
                {
                    services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(Generator);
                    services.AddSingleton<IChatClient>(ChatClient);
                }
            });
        });

    private static Task<HttpResponseMessage> AskAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync("/questions", body);

    [Fact]
    public async Task AnsweredReturnsAnswerAndSourcesInContextOrder()
    {
        using var app = CreateApp();

        var response = await AskAsync(app.CreateClient(), new { question = Question });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["status", "answer", "sources"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal("answered", body.GetProperty("status").GetString());
        Assert.Equal(ModelAnswer, body.GetProperty("answer").GetString());

        var sources = body.GetProperty("sources").EnumerateArray().ToList();
        Assert.Equal(3, sources.Count);
        Assert.Equal(["number", "document", "chunk", "score"], sources[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal([1, 2, 3], sources.Select(s => s.GetProperty("number").GetInt32()));
        Assert.Equal(["observabilidade.md", "dotnet.md", "observabilidade.md"], sources.Select(s => s.GetProperty("document").GetString()));
        Assert.Equal([3, 3, 2], sources.Select(s => s.GetProperty("chunk").GetInt32()));
        Assert.Equal(0.7694f, sources[0].GetProperty("score").GetSingle());
    }

    [Fact]
    public async Task RetrievedChunksAreSentToModelUsingConfiguredTopK()
    {
        using var app = CreateApp();

        await AskAsync(app.CreateClient(), new { question = Question });

        Assert.Equal(["search fonte-chunks 3"], Gateway.Operations);
        var prompt = Assert.Single(ChatClient.Calls).Messages[0].Text;
        Assert.All(Stored.Take(3), chunk => Assert.Contains(chunk.Content, prompt));
        Assert.DoesNotContain(Stored[3].Content, prompt);
    }

    [Fact]
    public async Task InsufficientContextReturnsFixedMessageAndNoSources()
    {
        ChatClient.Response = FakeChatClient.Json("insufficient_context", "");
        using var app = CreateApp();

        var response = await AskAsync(app.CreateClient(), new { question = "Qual é a receita de pão de queijo?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("insufficient_context", body.GetProperty("status").GetString());
        Assert.Equal(GeneratedAnswer.InsufficientContextMessage, body.GetProperty("answer").GetString());
        Assert.Empty(body.GetProperty("sources").EnumerateArray());
    }

    [Fact]
    public async Task NoContextReturnsFixedMessageWithoutCallingModel()
    {
        Gateway = GatewayWith([]);
        using var app = CreateApp();

        var response = await AskAsync(app.CreateClient(), new { question = Question });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("no_context", body.GetProperty("status").GetString());
        Assert.Equal(GeneratedAnswer.InsufficientContextMessage, body.GetProperty("answer").GetString());
        Assert.Empty(body.GetProperty("sources").EnumerateArray());
        Assert.Empty(ChatClient.Calls);
    }

    public static TheoryData<string, string> InvalidQuestions => new()
    {
        { "ausente", "{}" },
        { "nula", """{"question":null}""" },
        { "vazia", """{"question":""}""" },
        { "espaços", """{"question":"   "}""" },
        { "longa demais", JsonSerializer.Serialize(new { question = new string('a', QuestionEndpoint.MaxQuestionLength + 1) }) },
    };

    [Theory]
    [MemberData(nameof(InvalidQuestions))]
    public async Task InvalidQuestionReturnsValidationProblemWithoutCallingServices(string scenario, string json)
    {
        using var app = CreateApp();

        var response = await app.CreateClient().PostAsync("/questions", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("question", out _), scenario);
        Assert.Equal("Requisição inválida", body.GetProperty("title").GetString());
        Assert.Matches("^[0-9a-f]{32}$", body.GetProperty("traceId").GetString());
        Assert.DoesNotContain("aaaa", body.GetRawText());
        Assert.Empty(Generator.Calls);
        Assert.Empty(Gateway.Operations);
        Assert.Empty(ChatClient.Calls);
    }

    [Fact]
    public async Task QuestionAtMaximumLengthIsAccepted()
    {
        using var app = CreateApp();

        var response = await AskAsync(app.CreateClient(), new { question = new string('a', QuestionEndpoint.MaxQuestionLength) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ isto não é json")]
    public async Task MissingOrMalformedBodyReturnsBadRequest(string content)
    {
        using var app = CreateApp();

        var response = await app.CreateClient().PostAsync("/questions", new StringContent(content, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(Gateway.Operations);
    }

    [Fact]
    public async Task GeminiFailureReturnsServerError()
    {
        Generator = new FakeEmbeddingGenerator(Dimensions) { Failure = new HttpRequestException("falha no Gemini") };
        using var app = CreateApp();

        var response = await AskAsync(app.CreateClient(), new { question = Question });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task MissingAliasReturnsServerError()
    {
        Gateway = new FakeQdrantGateway { SearchFailure = new InvalidOperationException("Collection `fonte-chunks` doesn't exist!") };
        using var app = CreateApp();

        var response = await AskAsync(app.CreateClient(), new { question = Question });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(ChatClient.Calls);
    }

    [Fact]
    public async Task InvalidModelResponseReturnsServerError()
    {
        ChatClient.Response = FakeChatClient.Text("não é json");
        using var app = CreateApp();

        var response = await AskAsync(app.CreateClient(), new { question = Question });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task MissingGeminiKeyReturnsServerErrorLoggedAsConfigurationFailure()
    {
        using var app = CreateApp(withGemini: false);

        var response = await AskAsync(app.CreateClient(), new { question = Question });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(Gateway.Operations);
        var error = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Error && e.State.ContainsKey("Stage"));
        Assert.Equal("configuration", error.State["Stage"]);
    }

    [Fact]
    public async Task PipelineSpansShareTheHttpRequestParentAndTelemetryOmitsQuestionAndAnswer()
    {
        var traceId = ActivityTraceId.CreateRandom();
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Fonte.Api" or "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
        using var app = CreateApp();
        var request = new HttpRequestMessage(HttpMethod.Post, "/questions") { Content = JsonContent.Create(new { question = Question }) };
        var clientSpanId = ActivitySpanId.CreateRandom();
        request.Headers.Add("traceparent", $"00-{traceId}-{clientSpanId}-01");

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trace = _stopped.Where(a => a.TraceId == traceId).ToList();
        var pipeline = trace.Where(a => a.Source.Name == "Fonte.Api").OrderBy(a => a.StartTimeUtc).ToList();
        Assert.Equal(["embedding.create", "retrieval.search", "answer.generate"], pipeline.Select(a => a.OperationName));

        // Mesmo trace da requisição e um único pai, que não é o span do cliente: o span HTTP do servidor.
        // (O span do servidor termina depois da resposta chegar ao cliente, por isso não é aguardado aqui.)
        var parent = Assert.Single(pipeline.Select(a => a.ParentSpanId).Distinct());
        Assert.NotEqual(clientSpanId, parent);

        string[] forbidden = [Question, ModelAnswer];
        var texts = trace.SelectMany(a => a.TagObjects).Select(t => t.Value?.ToString() ?? "")
            .Concat(_logs.Entries.SelectMany(e => e.State.Values.Select(v => v?.ToString() ?? "").Append(e.Message)));
        Assert.All(texts, text => Assert.DoesNotContain(forbidden, f => text.Contains(f)));
    }

    [Fact]
    public void ToResponseMapsAnsweredWithNumberedSourcesInContextOrder()
    {
        var answer = new GeneratedAnswer(AnswerStatus.Answered, ModelAnswer, Stored.Take(2).ToList());

        var response = QuestionEndpoint.ToResponse(answer);

        Assert.Equal("answered", response.Status);
        Assert.Equal(ModelAnswer, response.Answer);
        Assert.Equal(
            [new QuestionEndpoint.QuestionSource(1, "observabilidade.md", 3, 0.7694f), new QuestionEndpoint.QuestionSource(2, "dotnet.md", 3, 0.6479f)],
            response.Sources);
    }

    [Theory]
    [InlineData(AnswerStatus.InsufficientContext, "insufficient_context")]
    [InlineData(AnswerStatus.NoContext, "no_context")]
    public void ToResponseOmitsSourcesWhenAnswerIsNotGrounded(AnswerStatus status, string expected)
    {
        var context = status == AnswerStatus.NoContext ? [] : Stored.ToList();
        var answer = new GeneratedAnswer(status, GeneratedAnswer.InsufficientContextMessage, context);

        var response = QuestionEndpoint.ToResponse(answer);

        Assert.Equal(expected, response.Status);
        Assert.Equal(GeneratedAnswer.InsufficientContextMessage, response.Answer);
        Assert.Empty(response.Sources);
    }
}
