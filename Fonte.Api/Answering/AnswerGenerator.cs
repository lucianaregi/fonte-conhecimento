using System.Diagnostics;
using System.Text.Json;
using Fonte.Api.Embeddings;
using Fonte.Api.Observability;
using Fonte.Api.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Fonte.Api.Answering;

/// <summary>
/// Gera a resposta a uma pergunta usando apenas os trechos recuperados como contexto.
/// As fontes retornadas são exatamente os trechos enviados, não uma escolha do modelo.
/// </summary>
public sealed partial class AnswerGenerator(
    Func<IChatClient> chatClientFactory,
    IOptions<GeminiOptions> options,
    AnswerMetrics metrics,
    TimeProvider timeProvider,
    ILogger<AnswerGenerator> logger)
{
    private readonly string _model = options.Value.GenerationModel;

    public async Task<GeneratedAnswer> GenerateAsync(
        string question,
        IReadOnlyList<RetrievedChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(chunks);

        var startedAt = timeProvider.GetTimestamp();
        var stage = Stages.Configuration;
        using var activity = FonteTelemetry.ActivitySource.StartActivity(FonteTelemetry.Spans.AnswerGenerate);
        activity?.SetTag(FonteTelemetry.Attributes.AnswerContextChunks, chunks.Count);
        activity?.SetTag(FonteTelemetry.Attributes.GenAiRequestModel, _model);

        try
        {
            GeneratedAnswer answer;

            if (chunks.Count == 0)
            {
                answer = new GeneratedAnswer(AnswerStatus.NoContext, GeneratedAnswer.InsufficientContextMessage, chunks);
            }
            else
            {
                // Resolvido aqui, e não no construtor: a falta de configuração é uma falha desta operação.
                var chatClient = chatClientFactory();

                stage = Stages.Generation;
                var response = await chatClient.GetResponseAsync(
                    [new ChatMessage(ChatRole.User, AnswerPrompt.BuildUserMessage(question, chunks, NewBoundary()))],
                    new ChatOptions
                    {
                        Instructions = AnswerPrompt.Instructions,
                        ResponseFormat = ChatResponseFormat.ForJsonSchema(AnswerPrompt.ResponseSchema),
                    },
                    cancellationToken);

                stage = Stages.Response;
                RecordUsage(activity, response.Usage);
                answer = ToAnswer(response, chunks);
            }

            var elapsed = timeProvider.GetElapsedTime(startedAt);
            var outcome = ToOutcome(answer.Status);
            activity?.SetTag(FonteTelemetry.Attributes.AnswerStatus, outcome);
            metrics.RecordAttempt(elapsed, outcome);
            LogGenerated(outcome, chunks.Count, (long)elapsed.TotalMilliseconds);

            return answer;
        }
        catch (Exception exception)
        {
            // Só o tipo: a mensagem pode conter dados que não devem ir para o trace.
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag(FonteTelemetry.Attributes.ErrorType, exception.GetType().FullName);
            LogFailed(stage, exception);
            metrics.RecordAttempt(timeProvider.GetElapsedTime(startedAt), AnswerMetrics.Outcomes.Failed, exception);
            throw;
        }
    }

    private static GeneratedAnswer ToAnswer(ChatResponse response, IReadOnlyList<RetrievedChunk> chunks)
    {
        // As mensagens citam só o tipo do problema, nunca o conteúdo da resposta.
        if (response.Messages.SelectMany(m => m.Contents).OfType<ErrorContent>().Any())
        {
            throw new InvalidOperationException("A geração foi bloqueada antes de produzir uma resposta.");
        }

        if (response.FinishReason is null)
        {
            throw new InvalidOperationException("A geração terminou sem motivo de término informado.");
        }

        if (response.FinishReason == ChatFinishReason.Length)
        {
            throw new InvalidOperationException("A resposta foi cortada pelo limite de tokens de saída.");
        }

        if (response.FinishReason != ChatFinishReason.Stop)
        {
            throw new InvalidOperationException($"A geração foi interrompida (motivo: {response.FinishReason.Value.Value}).");
        }

        var text = response.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("A resposta do modelo veio vazia.");
        }

        var (status, modelAnswer) = ParseModelOutput(text);

        return status switch
        {
            AnswerPrompt.StatusAnswered when !string.IsNullOrWhiteSpace(modelAnswer) =>
                new GeneratedAnswer(AnswerStatus.Answered, modelAnswer, chunks),
            AnswerPrompt.StatusAnswered =>
                throw new InvalidOperationException("O modelo indicou resposta, mas o campo 'answer' veio vazio."),
            AnswerPrompt.StatusInsufficientContext =>
                new GeneratedAnswer(AnswerStatus.InsufficientContext, GeneratedAnswer.InsufficientContextMessage, chunks),
            _ => throw new InvalidOperationException("O modelo retornou um status desconhecido."),
        };
    }

    private static (string? Status, string? Answer) ParseModelOutput(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String &&
                root.TryGetProperty("answer", out var answer) && answer.ValueKind == JsonValueKind.String)
            {
                return (status.GetString(), answer.GetString());
            }
        }
        catch (JsonException)
        {
            // Tratado abaixo, sem expor o texto recebido.
        }

        throw new InvalidOperationException("A resposta do modelo não é um JSON com os campos 'status' e 'answer'.");
    }

    private static void RecordUsage(Activity? activity, UsageDetails? usage)
    {
        if (usage?.InputTokenCount is { } input)
        {
            activity?.SetTag(FonteTelemetry.Attributes.GenAiInputTokens, input);
        }

        if (usage?.OutputTokenCount is { } output)
        {
            activity?.SetTag(FonteTelemetry.Attributes.GenAiOutputTokens, output);
        }
    }

    private static string ToOutcome(AnswerStatus status) => status switch
    {
        AnswerStatus.Answered => AnswerMetrics.Outcomes.Answered,
        AnswerStatus.InsufficientContext => AnswerMetrics.Outcomes.InsufficientContext,
        AnswerStatus.NoContext => AnswerMetrics.Outcomes.NoContext,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static string NewBoundary() => Guid.NewGuid().ToString("N")[..12];

    private static class Stages
    {
        public const string Configuration = "configuration";
        public const string Generation = "generation";
        public const string Response = "response";
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Resposta gerada: status {Status}, {Chunks} trechos de contexto, em {ElapsedMs} ms.")]
    private partial void LogGenerated(string status, int chunks, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha na geração de resposta na etapa {Stage}.")]
    private partial void LogFailed(string stage, Exception exception);
}
