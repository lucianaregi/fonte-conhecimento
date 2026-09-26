using Fonte.Api.Indexing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Fonte.Tests.Indexing;

public sealed class MarkdownDocumentReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fonte-tests", Guid.NewGuid().ToString("N"));

    public MarkdownDocumentReaderTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private MarkdownDocumentReader CreateReader(string path) =>
        new(
            Options.Create(new DocumentsOptions { Path = path }),
            new StubHostEnvironment(_root));

    private void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, "documents", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Fact]
    public async Task ReadsMarkdownFilesRecursivelyWithRelativePaths()
    {
        WriteFile("rag.md", "# RAG");
        WriteFile(Path.Combine("guias", "dotnet.md"), "# .NET");
        WriteFile(Path.Combine("guias", "observabilidade", "traces.md"), "# Traces");

        var documents = await CreateReader("documents").ReadAllAsync();

        Assert.Equal(
            [
                new MarkdownDocument("guias/dotnet.md", "# .NET"),
                new MarkdownDocument("guias/observabilidade/traces.md", "# Traces"),
                new MarkdownDocument("rag.md", "# RAG"),
            ],
            documents);
    }

    [Fact]
    public async Task IgnoresFilesThatAreNotMarkdown()
    {
        WriteFile("rag.md", "# RAG");
        WriteFile("notas.txt", "texto");
        WriteFile(Path.Combine("guias", "config.json"), "{}");

        var documents = await CreateReader("documents").ReadAllAsync();

        var document = Assert.Single(documents);
        Assert.Equal("rag.md", document.Path);
    }

    [Fact]
    public async Task AcceptsAbsolutePath()
    {
        WriteFile("rag.md", "# RAG");

        var documents = await CreateReader(Path.Combine(_root, "documents")).ReadAllAsync();

        Assert.Equal("rag.md", Assert.Single(documents).Path);
    }

    [Fact]
    public async Task ThrowsWhenFolderDoesNotExist()
    {
        var reader = CreateReader("inexistente");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => reader.ReadAllAsync());
    }

    private sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Fonte.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
