using Fonte.Api.Indexing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<DocumentsOptions>()
    .Bind(builder.Configuration.GetSection(DocumentsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<MarkdownDocumentReader>();
builder.Services.AddSingleton<MarkdownChunker>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok());

app.Run();

public partial class Program;
