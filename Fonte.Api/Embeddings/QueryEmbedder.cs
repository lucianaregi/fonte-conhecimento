using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Fonte.Api.Embeddings;

/// <summary>
/// Gera o embedding de uma pergunta com o mesmo modelo e dimensões usados nos chunks,
/// para que os vetores sejam comparáveis.
/// </summary>
public sealed class QueryEmbedder(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    IOptions<GeminiOptions> options)
{
    private readonly int _dimensions = options.Value.EmbeddingDimensions;

    public async Task<ReadOnlyMemory<float>> EmbedAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var generated = await generator.GenerateAsync([ToQueryText(question)], cancellationToken: cancellationToken);

        if (generated.Count != 1)
        {
            throw new InvalidOperationException($"Esperado 1 embedding para a pergunta, recebidos {generated.Count}.");
        }

        var vector = generated[0].Vector;

        if (vector.Length != _dimensions)
        {
            throw new InvalidOperationException(
                $"Esperado embedding da pergunta com {_dimensions} dimensões, recebidas {vector.Length}.");
        }

        return vector;
    }

    // Formato documentado para consultas no gemini-embedding-2, que não aceita task_type.
    private static string ToQueryText(string question) => $"task: search result | query: {question}";
}
