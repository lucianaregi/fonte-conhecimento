using System.ComponentModel;

namespace Fonte.Api.Indexing;

public static class DocumentIndexingEndpoint
{
    public sealed record IndexDocumentsResponse(
        [property: Description("Quantidade de documentos Markdown indexados.")] int Documents,
        [property: Description("Quantidade de chunks gravados no Qdrant.")] int Chunks,
        [property: Description("Falso quando a limpeza das collections antigas falhou; as sobras são removidas na próxima indexação.")] bool CleanupCompleted);

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
        })
            .WithName("IndexDocuments")
            .WithTags("Documentos")
            .WithSummary("Indexa ou reindexa os documentos Markdown da pasta configurada")
            .WithDescription(
                "Lê os documentos, divide em chunks, gera os embeddings no Gemini e publica uma nova collection no Qdrant " +
                "(reindexação blue/green). Sem corpo na requisição. Uma indexação por vez em cada instância. " +
                "Em qualquer falha antes da publicação, o índice ativo não muda.")
            .Produces<IndexDocumentsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }
}
