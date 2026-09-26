namespace Fonte.Api.VectorStore;

public sealed record ChunkPoint(Guid Id, string DocumentPath, int ChunkIndex, string Content, ReadOnlyMemory<float> Vector)
{
    public const string DocumentPathField = "document_path";
    public const string ChunkIndexField = "chunk_index";
    public const string ContentField = "content";
}
