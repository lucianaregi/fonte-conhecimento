namespace Fonte.Api.Retrieval;

/// <summary>Chunk recuperado pela busca vetorial; <see cref="Score"/> maior indica maior similaridade.</summary>
public sealed record RetrievedChunk(string DocumentPath, int ChunkIndex, string Content, float Score);
