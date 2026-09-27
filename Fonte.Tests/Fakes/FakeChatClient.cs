using Microsoft.Extensions.AI;

namespace Fonte.Tests.Fakes;

/// <summary>Registra as chamadas e devolve uma resposta configurável.</summary>
public sealed class FakeChatClient : IChatClient
{
    public ChatResponse Response { get; set; } = Json("answered", "Resposta.");

    /// <summary>Quando definido, toda chamada falha com esta exceção.</summary>
    public Exception? Failure { get; init; }

    public List<(List<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public List<CancellationToken> CancellationTokens { get; } = [];

    /// <summary>Resposta completa com o texto informado (normalmente o JSON do schema).</summary>
    public static ChatResponse Text(string text, ChatFinishReason? finishReason = null, UsageDetails? usage = null) =>
        new(new ChatMessage(ChatRole.Assistant, text))
        {
            FinishReason = finishReason ?? ChatFinishReason.Stop,
            Usage = usage,
        };

    public static ChatResponse Json(string status, string answer, UsageDetails? usage = null) =>
        Text($$"""{"status":"{{status}}","answer":{{System.Text.Json.JsonSerializer.Serialize(answer)}}}""", usage: usage);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((messages.ToList(), options));
        CancellationTokens.Add(cancellationToken);

        return Failure is not null ? Task.FromException<ChatResponse>(Failure) : Task.FromResult(Response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
