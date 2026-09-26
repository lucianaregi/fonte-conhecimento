using Fonte.Api.Indexing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Fonte.Api.Embeddings;

/// <summary>
/// Gera o embedding de cada chunk, um chunk por chamada e em sequência: a documentação
/// da Gemini API não informa quantas entradas cabem numa mesma requisição.
/// </summary>
public sealed class ChunkEmbedder(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    IOptions<GeminiOptions> options)
{
    private readonly int _dimensions = options.Value.EmbeddingDimensions;

    public async Task<IReadOnlyList<EmbeddedChunk>> EmbedAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var embedded = new List<EmbeddedChunk>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var generated = await generator.GenerateAsync([ToDocumentText(chunk)], cancellationToken: cancellationToken);

            if (generated.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Esperado 1 embedding para o chunk {chunk.Index} de '{chunk.DocumentPath}', recebidos {generated.Count}.");
            }

            var vector = generated[0].Vector;

            if (vector.Length != _dimensions)
            {
                throw new InvalidOperationException(
                    $"Esperado embedding com {_dimensions} dimensões para o chunk {chunk.Index} de '{chunk.DocumentPath}', recebidas {vector.Length}.");
            }

            embedded.Add(new EmbeddedChunk(chunk, vector));
        }

        return embedded;
    }

    // Formato documentado para documentos no gemini-embedding-2, que não aceita task_type.
    private static string ToDocumentText(DocumentChunk chunk) =>
        $"title: {chunk.DocumentPath} | text: {chunk.Content}";
}
