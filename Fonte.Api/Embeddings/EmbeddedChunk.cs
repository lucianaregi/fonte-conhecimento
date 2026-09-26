using Fonte.Api.Indexing;

namespace Fonte.Api.Embeddings;

public sealed record EmbeddedChunk(DocumentChunk Chunk, ReadOnlyMemory<float> Vector);
