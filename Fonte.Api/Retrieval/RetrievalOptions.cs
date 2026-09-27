using System.ComponentModel.DataAnnotations;

namespace Fonte.Api.Retrieval;

public sealed class RetrievalOptions
{
    public const string SectionName = "Retrieval";

    /// <summary>Quantidade máxima de chunks retornados por pergunta.</summary>
    [Range(1, int.MaxValue)]
    public int TopK { get; set; } = 3;
}
