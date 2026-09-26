namespace Fonte.Api.Indexing;

public sealed record DocumentChunk(string DocumentPath, int Index, string Content);
