using Microsoft.Extensions.Options;

namespace Fonte.Api.Indexing;

/// <summary>
/// Lê os arquivos <c>.md</c> da pasta configurada e de seus subdiretórios.
/// </summary>
public sealed class MarkdownDocumentReader(IOptions<DocumentsOptions> options, IHostEnvironment environment)
{
    private static readonly EnumerationOptions MarkdownFiles = new()
    {
        RecurseSubdirectories = true,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    private readonly string _root = Path.GetFullPath(options.Value.Path, environment.ContentRootPath);

    public async Task<IReadOnlyList<MarkdownDocument>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_root))
        {
            throw new DirectoryNotFoundException($"A pasta de documentos '{_root}' não existe.");
        }

        var files = Directory.EnumerateFiles(_root, "*.md", MarkdownFiles)
            .Select(file => (FullPath: file, RelativePath: ToDocumentPath(file)))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal);

        var documents = new List<MarkdownDocument>();

        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file.FullPath, cancellationToken);
            documents.Add(new MarkdownDocument(file.RelativePath, content));
        }

        return documents;
    }

    private string ToDocumentPath(string fullPath) =>
        Path.GetRelativePath(_root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
}
