using System.Diagnostics;

namespace Fonte.Api.Observability;

/// <summary>
/// Nomes e fonte de traces próprios do Fonte. Não registrar conteúdo de documentos ou chunks,
/// embeddings, chaves nem <c>DocumentPath</c> como atributos.
/// </summary>
public static class FonteTelemetry
{
    public const string Name = "Fonte.Api";

    public static readonly ActivitySource ActivitySource = new(Name);

    public static class Spans
    {
        public const string DocumentsRead = "documents.read";
        public const string DocumentsChunk = "documents.chunk";
        public const string EmbeddingCreate = "embedding.create";
        public const string VectorStoreReplace = "vectorstore.replace";
        public const string RetrievalSearch = "retrieval.search";
        public const string AnswerGenerate = "answer.generate";
    }

    public static class Attributes
    {
        public const string DocumentsCount = "fonte.documents.count";
        public const string ChunksCount = "fonte.chunks.count";
        public const string CleanupCompleted = "fonte.cleanup.completed";
        public const string IndexingOutcome = "fonte.indexing.outcome";
        public const string RetrievalTopK = "fonte.retrieval.top_k";
        public const string RetrievalResults = "fonte.retrieval.results";
        public const string RetrievalOutcome = "fonte.retrieval.outcome";
        public const string AnswerContextChunks = "fonte.answer.context_chunks";
        public const string AnswerStatus = "fonte.answer.status";
        public const string AnswerOutcome = "fonte.answer.outcome";
        public const string GenAiRequestModel = "gen_ai.request.model";
        public const string GenAiInputTokens = "gen_ai.usage.input_tokens";
        public const string GenAiOutputTokens = "gen_ai.usage.output_tokens";
        public const string ErrorType = "error.type";
    }
}
