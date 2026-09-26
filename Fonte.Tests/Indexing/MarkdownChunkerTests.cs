using Fonte.Api.Indexing;
using Microsoft.Extensions.Options;

namespace Fonte.Tests.Indexing;

public class MarkdownChunkerTests
{
    private static MarkdownChunker CreateChunker(int maxChunkSize) =>
        new(Options.Create(new DocumentsOptions { MaxChunkSize = maxChunkSize }));

    [Fact]
    public void ShortDocumentProducesSingleChunk()
    {
        var chunker = CreateChunker(100);
        var document = new MarkdownDocument("rag.md", "# RAG\n\nRetrieval-Augmented Generation.");

        var chunks = chunker.Chunk(document);

        var chunk = Assert.Single(chunks);
        Assert.Equal(new DocumentChunk("rag.md", 0, "# RAG\n\nRetrieval-Augmented Generation."), chunk);
    }

    [Fact]
    public void ParagraphsAreGroupedUntilMaxChunkSize()
    {
        var chunker = CreateChunker(20);
        var document = new MarkdownDocument("doc.md", "aaaaaaaa\n\nbbbbbbbb\n\ncccccccc");

        var chunks = chunker.Chunk(document);

        Assert.Equal(["aaaaaaaa\n\nbbbbbbbb", "cccccccc"], chunks.Select(c => c.Content));
    }

    [Fact]
    public void ParagraphLongerThanMaxChunkSizeIsSplitAtLastWhitespace()
    {
        var chunker = CreateChunker(12);
        var document = new MarkdownDocument("doc.md", "alpha beta gamma delta");

        var chunks = chunker.Chunk(document);

        Assert.Equal(["alpha beta", "gamma delta"], chunks.Select(c => c.Content));
    }

    [Fact]
    public void WordLongerThanMaxChunkSizeIsSplitAtLimit()
    {
        var chunker = CreateChunker(4);
        var document = new MarkdownDocument("doc.md", "abcdefghij");

        var chunks = chunker.Chunk(document);

        Assert.Equal(["abcd", "efgh", "ij"], chunks.Select(c => c.Content));
    }

    [Fact]
    public void SplitAtLimitDoesNotBreakSurrogatePair()
    {
        var chunker = CreateChunker(3);
        var document = new MarkdownDocument("doc.md", "ab😀cd");

        var chunks = chunker.Chunk(document);

        Assert.Equal(["ab", "😀c", "d"], chunks.Select(c => c.Content));
    }

    [Fact]
    public void WindowsLineEndingsSeparateParagraphs()
    {
        var chunker = CreateChunker(10);
        var document = new MarkdownDocument("doc.md", "aaaaaaaa\r\n\r\nbbbbbbbb");

        var chunks = chunker.Chunk(document);

        Assert.Equal(["aaaaaaaa", "bbbbbbbb"], chunks.Select(c => c.Content));
    }

    [Fact]
    public void ChunksHaveSequentialIndexesAndKeepDocumentPath()
    {
        var chunker = CreateChunker(20);
        var document = new MarkdownDocument("guias/dotnet.md", "primeiro paragrafo\n\nsegundo paragrafo\n\nterceiro paragrafo");

        var chunks = chunker.Chunk(document);

        Assert.Equal([0, 1, 2], chunks.Select(c => c.Index));
        Assert.All(chunks, c => Assert.Equal("guias/dotnet.md", c.DocumentPath));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(50)]
    public void NoChunkExceedsMaxChunkSize(int maxChunkSize)
    {
        var chunker = CreateChunker(maxChunkSize);
        var content = string.Join("\n\n", Enumerable.Range(1, 30).Select(i => $"Parágrafo {i} com algumas palavras e umapalavramuitolongasemespacos"));

        var chunks = chunker.Chunk(new MarkdownDocument("doc.md", content));

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.InRange(c.Content.Length, 1, maxChunkSize));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n\r\n  \n")]
    public void EmptyOrWhitespaceDocumentProducesNoChunks(string content)
    {
        var chunker = CreateChunker(100);

        var chunks = chunker.Chunk(new MarkdownDocument("vazio.md", content));

        Assert.Empty(chunks);
    }
}
