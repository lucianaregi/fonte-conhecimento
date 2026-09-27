using Fonte.Api.Retrieval;
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

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string collection,
        ReadOnlyMemory<float> vector,
        int limit,
        CancellationToken cancellationToken)
    {
        // Query API (o SearchAsync do SDK está obsoleto). O payload só vem quando pedido; vetores não são necessários.
        var points = await client.QueryAsync(
            collection,
            query: vector.ToArray(),
            limit: (ulong)limit,
            payloadSelector: true,
            cancellationToken: cancellationToken);

        return points.Select(ToRetrievedChunk).ToList();
    }

    public static RetrievedChunk ToRetrievedChunk(ScoredPoint point) =>
        new(
            RequiredPayload(point, ChunkPoint.DocumentPathField, Value.KindOneofCase.StringValue).StringValue,
            (int)RequiredPayload(point, ChunkPoint.ChunkIndexField, Value.KindOneofCase.IntegerValue).IntegerValue,
            RequiredPayload(point, ChunkPoint.ContentField, Value.KindOneofCase.StringValue).StringValue,
            point.Score);

    private static Value RequiredPayload(ScoredPoint point, string field, Value.KindOneofCase kind) =>
        point.Payload.TryGetValue(field, out var value) && value.KindCase == kind
            ? value
            : throw new InvalidOperationException($"O ponto {point.Id} não tem o campo '{field}' esperado no payload.");

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
