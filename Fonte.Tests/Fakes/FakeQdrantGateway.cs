using Fonte.Api.Retrieval;
using Fonte.Api.VectorStore;

namespace Fonte.Tests.Fakes;

/// <summary>
/// Registra as operações em ordem e pode falhar em operações específicas.
/// </summary>
public sealed class FakeQdrantGateway : IQdrantGateway
{
    public string? AliasTarget { get; set; }

    public List<string> Collections { get; } = [];

    public (string DocumentPath, Exception Error)? FailUpsertOf { get; init; }

    public (string Collection, Exception Error)? FailDeleteOf { get; init; }

    public List<string> Operations { get; } = [];

    /// <summary>Resultados devolvidos por <see cref="SearchAsync"/>.</summary>
    public List<RetrievedChunk> SearchResults { get; } = [];

    /// <summary>Quando definido, <see cref="SearchAsync"/> falha com esta exceção (ex.: alias inexistente).</summary>
    public Exception? SearchFailure { get; init; }

    public ReadOnlyMemory<float>? LastSearchVector { get; private set; }

    public List<IReadOnlyList<ChunkPoint>> Upserted { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    public int IndexOf(string operation)
    {
        var index = Operations.IndexOf(operation);
        Assert.True(index >= 0, $"Operação não executada: {operation}");
        return index;
    }

    public int LastIndexOf(string prefix) => Operations.FindLastIndex(o => o.StartsWith(prefix));

    public Task<string?> GetAliasTargetAsync(string alias, CancellationToken cancellationToken)
    {
        Record($"get-alias {alias}", cancellationToken);
        return Task.FromResult(AliasTarget);
    }

    public Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken)
    {
        Record("list", cancellationToken);
        return Task.FromResult<IReadOnlyList<string>>([.. Collections]);
    }

    public Task CreateCollectionAsync(string name, int dimensions, VectorDistance distance, CancellationToken cancellationToken)
    {
        Record($"create {name} {dimensions} {distance}", cancellationToken);
        Collections.Add(name);
        return Task.CompletedTask;
    }

    public Task CreateKeywordIndexAsync(string collection, string field, CancellationToken cancellationToken)
    {
        Record($"index {collection} {field}", cancellationToken);
        return Task.CompletedTask;
    }

    public Task UpsertAsync(string collection, IReadOnlyList<ChunkPoint> points, CancellationToken cancellationToken)
    {
        Record($"upsert {collection} {string.Join(",", points.Select(p => p.DocumentPath))}", cancellationToken);

        if (FailUpsertOf is { } failure && points.Any(p => p.DocumentPath == failure.DocumentPath))
        {
            return Task.FromException(failure.Error);
        }

        Upserted.Add(points);
        return Task.CompletedTask;
    }

    public Task SwitchAliasAsync(string alias, string newCollection, bool replaceExisting, CancellationToken cancellationToken)
    {
        Record($"switch {alias} {newCollection} replace={replaceExisting}", cancellationToken);
        AliasTarget = newCollection;
        return Task.CompletedTask;
    }

    public Task DeleteCollectionAsync(string name, CancellationToken cancellationToken)
    {
        Record($"delete {name}", cancellationToken);

        if (FailDeleteOf is { } failure && failure.Collection == name)
        {
            return Task.FromException(failure.Error);
        }

        Collections.Remove(name);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string collection,
        ReadOnlyMemory<float> vector,
        int limit,
        CancellationToken cancellationToken)
    {
        Record($"search {collection} {limit}", cancellationToken);
        LastSearchVector = vector;

        return SearchFailure is not null
            ? Task.FromException<IReadOnlyList<RetrievedChunk>>(SearchFailure)
            : Task.FromResult<IReadOnlyList<RetrievedChunk>>([.. SearchResults.Take(limit)]);
    }

    private void Record(string operation, CancellationToken cancellationToken)
    {
        Operations.Add(operation);
        Tokens.Add(cancellationToken);
    }
}
