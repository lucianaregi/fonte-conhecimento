using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Fonte.Api.ErrorHandling;
using Fonte.Api.Retrieval;

namespace Fonte.Api.Answering;

/// <summary>
/// <c>POST /questions</c>: coordena a recuperação (<see cref="ChunkRetriever"/>) e a geração
/// (<see cref="AnswerGenerator"/>). Logs, métricas e spans ficam nos próprios serviços.
/// </summary>
public static class QuestionEndpoint
{
    public const int MaxQuestionLength = 2000;

    // Required e MaxLength só descrevem o contrato na especificação OpenAPI: sem AddValidation, não validam
    // nada em tempo de execução. A validação e suas mensagens ficam em Validate.
    public sealed record QuestionRequest(
        [property: Required, MaxLength(MaxQuestionLength)]
        [property: Description("Pergunta a responder. Obrigatória, não pode ser vazia nem só espaços; até 2000 caracteres.")]
        string? Question);

    /// <param name="Number">Numeração <c>[n]</c> do trecho no prompt enviado ao modelo.</param>
    public sealed record QuestionSource(
        [property: Description("Numeração [n] do trecho no prompt enviado ao modelo.")] int Number,
        [property: Description("Caminho do documento relativo à pasta configurada.")] string Document,
        [property: Description("Posição do chunk no documento, a partir de 0.")] int Chunk,
        [property: Description("Similaridade da busca vetorial (Cosine; maior é mais similar).")] float Score);

    public sealed record QuestionResponse(
        [property: Description("answered, insufficient_context ou no_context.")] string Status,
        [property: Description("Resposta do modelo ou, sem contexto suficiente, a mensagem fixa do Fonte.")] string Answer,
        [property: Description("Trechos enviados como contexto, na ordem; vazio quando o status não é answered.")] IReadOnlyList<QuestionSource> Sources);

    public static IEndpointRouteBuilder MapQuestions(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/questions", async (
            QuestionRequest request,
            ChunkRetriever retriever,
            AnswerGenerator generator,
            CancellationToken cancellationToken) =>
        {
            // A mensagem de validação nunca repete a pergunta.
            if (Validate(request.Question) is { } error)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["question"] = [error] },
                    title: ProblemDetailsRegistration.BadRequestTitle);
            }

            var chunks = await retriever.RetrieveAsync(request.Question!, cancellationToken);
            var answer = await generator.GenerateAsync(request.Question!, chunks, cancellationToken);

            return Results.Ok(ToResponse(answer));
        })
            .WithName("AskQuestion")
            .WithTags("Perguntas")
            .WithSummary("Responde a uma pergunta com base nos documentos indexados")
            .WithDescription(
                "Gera o embedding da pergunta, recupera os chunks mais relevantes no Qdrant e pede ao Gemini uma resposta " +
                "fundamentada apenas nesses trechos. Exige uma indexação prévia (POST /documents/index). " +
                "O 400 traz errors.question quando a pergunta é inválida; com corpo ausente ou JSON malformado, vem sem errors.")
            .Produces<QuestionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    public static QuestionResponse ToResponse(GeneratedAnswer answer) => answer.Status switch
    {
        AnswerStatus.Answered => new QuestionResponse(
            "answered",
            answer.Text,
            answer.Context.Select((chunk, i) => new QuestionSource(i + 1, chunk.DocumentPath, chunk.ChunkIndex, chunk.Score)).ToList()),
        // Sem resposta fundamentada, não há fontes a citar.
        AnswerStatus.InsufficientContext => new QuestionResponse("insufficient_context", answer.Text, []),
        AnswerStatus.NoContext => new QuestionResponse("no_context", answer.Text, []),
        _ => throw new InvalidOperationException($"Status de resposta desconhecido: {answer.Status}."),
    };

    private static string? Validate(string? question) =>
        string.IsNullOrWhiteSpace(question) ? "A pergunta é obrigatória."
        : question.Length > MaxQuestionLength ? $"A pergunta deve ter no máximo {MaxQuestionLength} caracteres."
        : null;
}
