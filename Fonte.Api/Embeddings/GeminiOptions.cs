using System.ComponentModel.DataAnnotations;

namespace Fonte.Api.Embeddings;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>
    /// Chave da Gemini API. Vem do <c>appsettings.Development.json</c> local (não versionado) em
    /// desenvolvimento ou de <c>Gemini__ApiKey</c> nos demais ambientes. Não é validada na
    /// inicialização: só é exigida ao criar o cliente.
    /// </summary>
    public string? ApiKey { get; set; }

    [Required]
    public string EmbeddingModel { get; set; } = "gemini-embedding-2";

    /// <summary>
    /// Modelo generativo usado para responder perguntas. <c>gemini-3.6-flash</c>: estável e com plano
    /// gratuito na consulta de 26/09/2026 (ver plano 006).
    /// </summary>
    [Required]
    public string GenerationModel { get; set; } = "gemini-3.6-flash";

    /// <summary>Faixa documentada para <c>gemini-embedding-2</c>.</summary>
    [Range(128, 3072)]
    public int EmbeddingDimensions { get; set; } = 768;
}
