using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Fonte.Api.Answering;
using Fonte.Api.Embeddings;
using Fonte.Api.Observability;
using Fonte.Api.Retrieval;
using Fonte.Tests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fonte.Tests.Answering;

public sealed class AnswerGeneratorTests : IDisposable
{
    private const string Model = "modelo-de-teste";
    private const string Question = "Qual é a função do ActivitySource?";
    private const string ModelAnswer = "O ActivitySource cria spans para traces próprios.";

    private static readonly ActivitySource TestSource = new("Fonte.Tests.Answering");

    private static readonly RetrievedChunk[] Chunks =
    [
        new("observabilidade.md", 3, "O `ActivitySource` é o ponto de partida para criar traces.", 0.77f),
        new("dotnet.md", 3, "A configuração de uma aplicação ASP.NET Core é montada em sequência.", 0.65f),
    ];

    private readonly ServiceProvider _metricsServices = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly CapturingLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    public AnswerGeneratorTests()
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

    private FakeChatClient ChatClient { get; set; } = new() { Response = FakeChatClient.Json("answered", ModelAnswer) };

    private Func<IChatClient>? ChatClientFactory { get; set; }

    private IMeterFactory MeterFactory => _metricsServices.GetRequiredService<IMeterFactory>();

    private AnswerGenerator CreateGenerator() =>
        new(
            ChatClientFactory ?? (() => ChatClient),
            Options.Create(new GeminiOptions { GenerationModel = Model }),
            new AnswerMetrics(MeterFactory),
            TimeProvider.System,
            _loggerFactory.CreateLogger<AnswerGenerator>());

    [Fact]
    public async Task SendsSystemInstructionsJsonSchemaAndDelimitedPrompt()
    {
        await CreateGenerator().GenerateAsync(Question, Chunks);

        var (messages, options) = Assert.Single(ChatClient.Calls);
        Assert.Equal(AnswerPrompt.Instructions, options!.Instructions);
        var format = Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
        Assert.Equal(AnswerPrompt.ResponseSchema.GetRawText(), format.Schema!.Value.GetRawText());

        var message = Assert.Single(messages);
        Assert.Equal(ChatRole.User, message.Role);
        var boundary = Regex.Match(message.Text, "<pergunta-([0-9a-f]{12})>").Groups[1].Value;
        Assert.Equal(AnswerPrompt.BuildUserMessage(Question, Chunks, boundary), message.Text);
    }

    [Fact]
    public async Task UsesNewBoundaryForEachRequest()
    {
        var generator = CreateGenerator();

        await generator.GenerateAsync(Question, Chunks);
        await generator.GenerateAsync(Question, Chunks);

        var boundaries = ChatClient.Calls
            .Select(c => Regex.Match(c.Messages[0].Text, "<pergunta-([0-9a-f]{12})>").Groups[1].Value)
            .ToList();
        Assert.All(boundaries, b => Assert.Equal(12, b.Length));
        Assert.NotEqual(boundaries[0], boundaries[1]);
    }

    [Fact]
    public async Task AnsweredReturnsModelTextAndExactlyTheChunksSentAsContext()
    {
        var answer = await CreateGenerator().GenerateAsync(Question, Chunks);

        Assert.Equal(AnswerStatus.Answered, answer.Status);
        Assert.Equal(ModelAnswer, answer.Text);
        Assert.Equal(Chunks, answer.Context);
    }

    [Fact]
    public async Task InsufficientContextReturnsFixedMessageAndKeepsContext()
    {
        ChatClient.Response = FakeChatClient.Json("insufficient_context", "Qualquer texto do modelo.");

        var answer = await CreateGenerator().GenerateAsync(Question, Chunks);

        Assert.Equal(AnswerStatus.InsufficientContext, answer.Status);
        Assert.Equal(GeneratedAnswer.InsufficientContextMessage, answer.Text);
        Assert.Equal(Chunks, answer.Context);
    }

    [Fact]
    public async Task EmptyContextReturnsNoContextWithoutCallingModel()
    {
        var answer = await CreateGenerator().GenerateAsync(Question, []);

        Assert.Equal(new GeneratedAnswer(AnswerStatus.NoContext, GeneratedAnswer.InsufficientContextMessage, []).Status, answer.Status);
        Assert.Equal(GeneratedAnswer.InsufficientContextMessage, answer.Text);
        Assert.Empty(answer.Context);
        Assert.Empty(ChatClient.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyQuestionThrowsWithoutCallingModel(string question)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CreateGenerator().GenerateAsync(question, Chunks));

        Assert.Empty(ChatClient.Calls);
    }

    public static TheoryData<string, ChatResponse> InvalidResponses => new()
    {
        { "prompt bloqueado", new ChatResponse(new ChatMessage(ChatRole.Assistant, [new ErrorContent("bloqueado")])) },
        { "sem motivo de término", WithoutFinishReason(FakeChatClient.Text("""{"status":"answered","answer":"x"}""")) },
        { "cortada", FakeChatClient.Text("""{"status":"answered","answer":"x"}""", ChatFinishReason.Length) },
        { "filtro", FakeChatClient.Text("""{"status":"answered","answer":"x"}""", ChatFinishReason.ContentFilter) },
        { "texto vazio", FakeChatClient.Text("   ") },
        { "json inválido", FakeChatClient.Text("não é json") },
        { "sem status", FakeChatClient.Text("""{"answer":"x"}""") },
        { "sem answer", FakeChatClient.Text("""{"status":"answered"}""") },
        { "status desconhecido", FakeChatClient.Json("talvez", "x") },
        { "answered vazio", FakeChatClient.Json("answered", "  ") },
    };

    private static ChatResponse WithoutFinishReason(ChatResponse response)
    {
        response.FinishReason = null;
        return response;
    }

    [Theory]
    [MemberData(nameof(InvalidResponses))]
    public async Task InvalidOrIncompleteResponseThrowsAndIsLoggedAsResponseFailure(string scenario, ChatResponse response)
    {
        ChatClient.Response = response;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateGenerator().GenerateAsync(Question, Chunks));

        Assert.DoesNotContain(ModelAnswer, exception.Message);
        Assert.Equal("response", Assert.Single(_logs.Entries, e => e.Level == LogLevel.Error).State["Stage"]);
        Assert.NotNull(scenario);
    }

    [Fact]
    public async Task ModelCallFailurePropagatesAndIsLoggedAsGenerationFailure()
    {
        var failure = new HttpRequestException("429");
        ChatClient = new FakeChatClient { Failure = failure };

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => CreateGenerator().GenerateAsync(Question, Chunks));

        Assert.Same(failure, exception);
        Assert.Equal("generation", Assert.Single(_logs.Entries, e => e.Level == LogLevel.Error).State["Stage"]);
    }

    [Fact]
    public async Task ConfigurationFailureIsLoggedWithoutCallingModel()
    {
        ChatClientFactory = () => throw new InvalidOperationException("A chave da Gemini API não está configurada em 'Gemini:ApiKey'.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateGenerator().GenerateAsync(Question, Chunks));

        Assert.Empty(ChatClient.Calls);
        Assert.Equal("configuration", Assert.Single(_logs.Entries, e => e.Level == LogLevel.Error).State["Stage"]);
    }

    [Fact]
    public async Task PassesCancellationTokenToModel()
    {
        using var cancellation = new CancellationTokenSource();

        await CreateGenerator().GenerateAsync(Question, Chunks, cancellation.Token);

        Assert.Equal(cancellation.Token, Assert.Single(ChatClient.CancellationTokens));
    }

    [Fact]
    public async Task SpanHasContextStatusModelAndTokenUsage()
    {
        ChatClient.Response = FakeChatClient.Json("answered", ModelAnswer, new UsageDetails { InputTokenCount = 420, OutputTokenCount = 35 });

        var span = Assert.Single(await GenerateWithinParentAsync());

        Assert.Equal("answer.generate", span.OperationName);
        Assert.Equal(2, span.GetTagItem("fonte.answer.context_chunks"));
        Assert.Equal("answered", span.GetTagItem("fonte.answer.status"));
        Assert.Equal(Model, span.GetTagItem("gen_ai.request.model"));
        Assert.Equal(420L, span.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(35L, span.GetTagItem("gen_ai.usage.output_tokens"));
    }

    [Fact]
    public async Task SpanOmitsTokenUsageWhenNotReported()
    {
        var span = Assert.Single(await GenerateWithinParentAsync());

        Assert.Null(span.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Null(span.GetTagItem("gen_ai.usage.output_tokens"));
    }

    [Fact]
    public async Task FailedGenerationMarksSpanAsError()
    {
        ChatClient.Response = FakeChatClient.Text("não é json");

        var span = Assert.Single(await GenerateWithinParentAsync(expectFailure: true));

        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, span.GetTagItem("error.type"));
    }

    [Fact]
    public async Task TelemetryNeverContainsQuestionContextPromptOrAnswer()
    {
        var spans = await GenerateWithinParentAsync();
        ChatClient.Response = FakeChatClient.Text("{\"status\":\"answered\",\"answer\":");
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateGenerator().GenerateAsync(Question, Chunks));

        string[] forbidden = [Question, ModelAnswer, Chunks[0].Content, Chunks[1].Content, "observabilidade.md", "dotnet.md", "<trecho-", "<pergunta-"];
        var spanValues = spans.SelectMany(s => s.TagObjects).Select(t => t.Value?.ToString() ?? "");
        var logValues = _logs.Entries.SelectMany(e => e.State.Values.Select(v => v?.ToString() ?? "").Append(e.Message).Append(e.Exception?.Message ?? ""));
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
            if (instrument.Meter.Scope == MeterFactory && instrument.Name == "fonte.answer.duration")
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

        var generator = CreateGenerator();
        await generator.GenerateAsync(Question, Chunks);
        ChatClient.Response = FakeChatClient.Json("insufficient_context", "");
        await generator.GenerateAsync(Question, Chunks);
        await generator.GenerateAsync(Question, []);
        ChatClient.Response = FakeChatClient.Text("não é json");
        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(Question, Chunks));

        var recorded = outcomes.ToList();
        Assert.Equal(
            ["answered", "insufficient_context", "no_context", "failed"],
            recorded.Select(r => r["fonte.answer.outcome"]));
        Assert.Equal(typeof(InvalidOperationException).FullName, recorded[3]["error.type"]);
        Assert.All(recorded.Take(3), r => Assert.False(r.ContainsKey("error.type")));
    }

    private async Task<List<Activity>> GenerateWithinParentAsync(bool expectFailure = false)
    {
        var parent = TestSource.StartActivity("teste")!;

        try
        {
            await CreateGenerator().GenerateAsync(Question, Chunks);
        }
        catch when (expectFailure)
        {
        }
        finally
        {
            parent.Stop();
        }

        return _stopped
            .Where(a => a.TraceId == parent.TraceId && a.Source.Name == FonteTelemetry.Name)
            .ToList();
    }
}
