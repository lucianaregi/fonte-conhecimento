namespace Fonte.Api.Indexing;

public static class DocumentIndexingEndpoint
{
    public sealed record IndexDocumentsResponse(int Documents, int Chunks, bool CleanupCompleted);

    public static IEndpointRouteBuilder MapDocumentIndexing(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/documents/index", async (DocumentIndexer indexer, CancellationToken cancellationToken) =>
        {
            var outcome = await indexer.IndexAsync(cancellationToken);

            return outcome.Status switch
            {
                DocumentIndexingStatus.Published => Results.Ok(
                    new IndexDocumentsResponse(outcome.Documents, outcome.Chunks, outcome.CleanupCompleted)),
                DocumentIndexingStatus.AlreadyRunning => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Indexação em andamento",
                    detail: "Já existe uma indexação em andamento. Tente novamente quando ela terminar."),
                DocumentIndexingStatus.NoDocuments => Results.Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Nenhum documento encontrado",
                    detail: "Nenhum documento Markdown foi encontrado na pasta configurada. O índice ativo não foi alterado."),
                _ => throw new InvalidOperationException($"Status de indexação desconhecido: {outcome.Status}."),
            };
        });

        return endpoints;
    }
}
