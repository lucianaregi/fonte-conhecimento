using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Fonte.Api.Indexing;

/// <summary>
/// Divide o documento em parágrafos (blocos separados por linha em branco) e agrupa
/// parágrafos consecutivos até <see cref="DocumentsOptions.MaxChunkSize"/> caracteres.
/// Parágrafos maiores que o limite são quebrados no último espaço antes dele.
/// </summary>
public sealed partial class MarkdownChunker(IOptions<DocumentsOptions> options)
{
    private const string ParagraphSeparator = "\n\n";

    private readonly int _maxChunkSize = options.Value.MaxChunkSize;

    public IReadOnlyList<DocumentChunk> Chunk(MarkdownDocument document)
    {
        var chunks = new List<DocumentChunk>();
        var current = string.Empty;

        foreach (var segment in SplitIntoSegments(document.Content))
        {
            if (current.Length == 0)
            {
                current = segment;
            }
            else if (current.Length + ParagraphSeparator.Length + segment.Length <= _maxChunkSize)
            {
                current += ParagraphSeparator + segment;
            }
            else
            {
                chunks.Add(new DocumentChunk(document.Path, chunks.Count, current));
                current = segment;
            }
        }

        if (current.Length > 0)
        {
            chunks.Add(new DocumentChunk(document.Path, chunks.Count, current));
        }

        return chunks;
    }

    private IEnumerable<string> SplitIntoSegments(string content)
    {
        var paragraphs = BlankLine().Split(content.ReplaceLineEndings("\n"));

        foreach (var paragraph in paragraphs)
        {
            var remaining = paragraph.Trim();

            while (remaining.Length > _maxChunkSize)
            {
                var cut = FindCut(remaining);
                yield return remaining[..cut].TrimEnd();
                remaining = remaining[cut..].TrimStart();
            }

            if (remaining.Length > 0)
            {
                yield return remaining;
            }
        }
    }

    private int FindCut(string text)
    {
        for (var i = _maxChunkSize; i > 0; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        // Sem espaço antes do limite: corta no limite, sem separar um par substituto (ex.: emoji).
        return char.IsHighSurrogate(text[_maxChunkSize - 1]) && _maxChunkSize > 1
            ? _maxChunkSize - 1
            : _maxChunkSize;
    }

    [GeneratedRegex(@"\n[ \t]*\n\s*")]
    private static partial Regex BlankLine();
}
