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

    public sealed record QuestionRequest(string? Question);

    /// <param name="Number">Numeração <c>[n]</c> do trecho no prompt enviado ao modelo.</param>
    public sealed record QuestionSource(int Number, string Document, int Chunk, float Score);

    public sealed record QuestionResponse(string Status, string Answer, IReadOnlyList<QuestionSource> Sources);

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
        });

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
