using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Fonte.Api.VectorStore;

/// <summary>
/// Implementação de <see cref="IQdrantGateway"/> com o SDK oficial. Único ponto do Fonte que conhece o SDK.
/// </summary>
public sealed class QdrantGateway(QdrantClient client) : IQdrantGateway
{
    public async Task<string?> GetAliasTargetAsync(string alias, CancellationToken cancellationToken)
    {
        var aliases = await client.ListAliasesAsync(cancellationToken);
        return aliases.FirstOrDefault(a => a.AliasName == alias)?.CollectionName;
    }

    public Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken) =>
        client.ListCollectionsAsync(cancellationToken);

    public Task CreateCollectionAsync(string name, int dimensions, VectorDistance distance, CancellationToken cancellationToken) =>
        client.CreateCollectionAsync(name, ToVectorParams(dimensions, distance), cancellationToken: cancellationToken);

    public Task CreateKeywordIndexAsync(string collection, string field, CancellationToken cancellationToken) =>
        client.CreatePayloadIndexAsync(collection, field, PayloadSchemaType.Keyword, cancellationToken: cancellationToken);

    public Task UpsertAsync(string collection, IReadOnlyList<ChunkPoint> points, CancellationToken cancellationToken) =>
        client.UpsertAsync(collection, points.Select(ToPointStruct).ToList(), wait: true, cancellationToken: cancellationToken);

    public Task SwitchAliasAsync(string alias, string newCollection, bool replaceExisting, CancellationToken cancellationToken)
    {
        var operations = new List<AliasOperations>();

        if (replaceExisting)
        {
            operations.Add(new AliasOperations { DeleteAlias = new DeleteAlias { AliasName = alias } });
        }

        operations.Add(new AliasOperations { CreateAlias = new CreateAlias { AliasName = alias, CollectionName = newCollection } });

        // Uma única requisição: o Qdrant aplica as operações de alias atomicamente.
        return client.UpdateAliasesAsync(operations, cancellationToken: cancellationToken);
    }

    public Task DeleteCollectionAsync(string name, CancellationToken cancellationToken) =>
        client.DeleteCollectionAsync(name, cancellationToken: cancellationToken);

    public static PointStruct ToPointStruct(ChunkPoint point) =>
        new()
        {
            Id = point.Id,
            Vectors = point.Vector.ToArray(),
            Payload =
            {
                [ChunkPoint.DocumentPathField] = point.DocumentPath,
                [ChunkPoint.ChunkIndexField] = point.ChunkIndex,
                [ChunkPoint.ContentField] = point.Content,
            },
        };

    public static VectorParams ToVectorParams(int dimensions, VectorDistance distance) =>
        new()
        {
            Size = (ulong)dimensions,
            Distance = distance switch
            {
                VectorDistance.Cosine => Distance.Cosine,
                _ => throw new ArgumentOutOfRangeException(nameof(distance), distance, null),
            },
        };
}
