using System.Globalization;
using System.Text.RegularExpressions;
using Fonte.Api.Embeddings;
using Microsoft.Extensions.Options;

namespace Fonte.Api.VectorStore;

/// <summary>
/// Reindexação completa blue/green: grava tudo numa collection nova, aponta o alias para ela
/// e só então apaga as collections antigas.
/// </summary>
public sealed class ChunkVectorStore(
    IQdrantGateway gateway,
    IOptions<QdrantOptions> qdrantOptions,
    IOptions<GeminiOptions> geminiOptions,
    TimeProvider timeProvider)
{
    private readonly string _alias = qdrantOptions.Value.CollectionName;
    private readonly int _dimensions = geminiOptions.Value.EmbeddingDimensions;
    private readonly Regex _managedCollection = new($"^{Regex.Escape(qdrantOptions.Value.CollectionName)}-[0-9]{{17}}$");

    /// <summary>
    /// Publica <paramref name="chunks"/> como a nova indexação ativa.
    /// </summary>
    /// <exception cref="VectorStoreCleanupException">
    /// A nova indexação foi publicada, mas a remoção das collections antigas falhou.
    /// Qualquer outra exceção significa que a publicação falhou e o alias não foi alterado.
    /// </exception>
    public async Task ReplaceAllAsync(IReadOnlyList<EmbeddedChunk> chunks, CancellationToken cancellationToken = default)
    {
        var currentTarget = await gateway.GetAliasTargetAsync(_alias, cancellationToken);
        var collection = string.Create(CultureInfo.InvariantCulture, $"{_alias}-{timeProvider.GetUtcNow():yyyyMMddHHmmssfff}");

        await gateway.CreateCollectionAsync(collection, _dimensions, VectorDistance.Cosine, cancellationToken);
        await gateway.CreateKeywordIndexAsync(collection, ChunkPoint.DocumentPathField, cancellationToken);

        foreach (var document in chunks.GroupBy(c => c.Chunk.DocumentPath, StringComparer.Ordinal))
        {
            await gateway.UpsertAsync(collection, document.Select(ToPoint).ToList(), cancellationToken);
        }

        await gateway.SwitchAliasAsync(_alias, collection, replaceExisting: currentTarget is not null, cancellationToken);

        await DeleteOldCollectionsAsync(collection, cancellationToken);
    }

    private async Task DeleteOldCollectionsAsync(string activeCollection, CancellationToken cancellationToken)
    {
        string? current = null;

        try
        {
            var collections = await gateway.ListCollectionsAsync(cancellationToken);

            foreach (var old in collections.Where(c => c != activeCollection && _managedCollection.IsMatch(c)))
            {
                current = old;
                await gateway.DeleteCollectionAsync(old, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            // Inclui cancelamento: a publicação já aconteceu e o chamador precisa saber disso.
            throw new VectorStoreCleanupException(activeCollection, current, exception);
        }
    }

    private static ChunkPoint ToPoint(EmbeddedChunk embedded) =>
        new(
            ChunkPointId.For(embedded.Chunk.DocumentPath, embedded.Chunk.Index),
            embedded.Chunk.DocumentPath,
            embedded.Chunk.Index,
            embedded.Chunk.Content,
            embedded.Vector);
}
