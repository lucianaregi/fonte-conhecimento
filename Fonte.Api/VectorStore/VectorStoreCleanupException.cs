namespace Fonte.Api.VectorStore;

/// <summary>
/// A nova indexação foi publicada (o alias já aponta para <see cref="ActiveCollection"/>),
/// mas a remoção das collections antigas falhou. As sobras são removidas na próxima
/// reindexação bem-sucedida.
/// </summary>
public sealed class VectorStoreCleanupException(string activeCollection, string? failedCollection, Exception innerException)
    : Exception(
        failedCollection is null
            ? $"A indexação '{activeCollection}' está ativa, mas a limpeza das collections antigas falhou."
            : $"A indexação '{activeCollection}' está ativa, mas a collection antiga '{failedCollection}' não pôde ser apagada.",
        innerException)
{
    public string ActiveCollection { get; } = activeCollection;

    public string? FailedCollection { get; } = failedCollection;
}
