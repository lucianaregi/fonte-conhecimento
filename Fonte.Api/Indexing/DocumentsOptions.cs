using System.ComponentModel.DataAnnotations;

namespace Fonte.Api.Indexing;

public sealed class DocumentsOptions
{
    public const string SectionName = "Documents";

    [Required]
    public string Path { get; set; } = "documents";

    [Range(1, int.MaxValue)]
    public int MaxChunkSize { get; set; } = 1000;
}
