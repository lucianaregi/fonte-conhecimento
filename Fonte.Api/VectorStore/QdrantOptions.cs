using System.ComponentModel.DataAnnotations;

namespace Fonte.Api.VectorStore;

public sealed class QdrantOptions
{
    public const string SectionName = "Qdrant";

    /// <summary>Alias pelo qual a collection ativa é acessada.</summary>
    [Required]
    public string CollectionName { get; set; } = "fonte-chunks";

    /// <summary>
    /// Endereço gRPC do cluster (porta 6334). Segredo: <c>appsettings.Development.json</c> local
    /// (não versionado) em desenvolvimento ou <c>Qdrant__Url</c> nos demais ambientes.
    /// Só é exigido ao criar o cliente.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Segredo: <c>appsettings.Development.json</c> local (não versionado) em desenvolvimento ou
    /// <c>Qdrant__ApiKey</c> nos demais ambientes. Só é exigido ao criar o cliente.
    /// </summary>
    public string? ApiKey { get; set; }
}
