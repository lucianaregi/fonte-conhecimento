using Fonte.Api.Retrieval;

namespace Fonte.Api.VectorStore;

/// <summary>
/// Operações do Qdrant usadas pelo Fonte, expressas apenas com tipos do Fonte.
/// </summary>
public interface IQdrantGateway
{
    Task<string?> GetAliasTargetAsync(string alias, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken);

    Task CreateCollectionAsync(string name, int dimensions, VectorDistance distance, CancellationToken cancellationToken);

    Task CreateKeywordIndexAsync(string collection, string field, CancellationToken cancellationToken);

    Task UpsertAsync(string collection, IReadOnlyList<ChunkPoint> points, CancellationToken cancellationToken);

    /// <summary>Aponta o alias para <paramref name="newCollection"/> numa única operação atômica.</summary>
    Task SwitchAliasAsync(string alias, string newCollection, bool replaceExisting, CancellationToken cancellationToken);

    Task DeleteCollectionAsync(string name, CancellationToken cancellationToken);

    /// <summary>Os <paramref name="limit"/> pontos mais similares a <paramref name="vector"/>, ordenados por score.</summary>
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string collection,
        ReadOnlyMemory<float> vector,
        int limit,
        CancellationToken cancellationToken);
}
