using Fonte.Api.Retrieval;

namespace Fonte.Api.Answering;

public enum AnswerStatus
{
    /// <summary>O modelo respondeu com base no contexto.</summary>
    Answered,

    /// <summary>O modelo indicou que os trechos enviados não bastam para responder.</summary>
    InsufficientContext,

    /// <summary>Nenhum trecho foi recuperado; o modelo não foi chamado.</summary>
    NoContext,
}

/// <param name="Text">Resposta do modelo ou, quando não houve resposta, a mensagem fixa do Fonte.</param>
/// <param name="Context">
/// Trechos enviados ao modelo, na ordem do prompt: <c>Context[i]</c> é o trecho <c>[i + 1]</c>.
/// São as fontes da resposta; vêm da recuperação, não de uma escolha do modelo.
/// </param>
public sealed record GeneratedAnswer(AnswerStatus Status, string Text, IReadOnlyList<RetrievedChunk> Context)
{
    public const string InsufficientContextMessage =
        "Os documentos disponíveis não contêm informação suficiente para responder a esta pergunta.";
}
