using Fonte.Api.Embeddings;
using Fonte.Api.Indexing;
using Google.GenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<DocumentsOptions>()
    .Bind(builder.Configuration.GetSection(DocumentsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<MarkdownDocumentReader>();
builder.Services.AddSingleton<MarkdownChunker>();

builder.Services.AddOptions<GeminiOptions>()
    .Bind(builder.Configuration.GetSection(GeminiOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(services =>
{
    var options = services.GetRequiredService<IOptions<GeminiOptions>>().Value;

    if (string.IsNullOrWhiteSpace(options.ApiKey))
    {
        throw new InvalidOperationException(
            $"A chave da Gemini API não está configurada em '{GeminiOptions.SectionName}:{nameof(GeminiOptions.ApiKey)}'.");
    }

    // Chave e modo explícitos, sem depender de GEMINI_API_KEY, GOOGLE_API_KEY ou GOOGLE_GENAI_USE_VERTEXAI.
    return new Client(apiKey: options.ApiKey, vertexAI: false);
});
builder.Services.AddSingleton(services =>
{
    var options = services.GetRequiredService<IOptions<GeminiOptions>>().Value;
    return services.GetRequiredService<Client>().AsIEmbeddingGenerator(options.EmbeddingModel, options.EmbeddingDimensions);
});
builder.Services.AddSingleton<ChunkEmbedder>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok());

app.Run();

public partial class Program;
