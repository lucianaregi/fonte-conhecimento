using System.Diagnostics;
using Fonte.Api.Embeddings;
using Fonte.Api.Observability;
using Fonte.Api.VectorStore;

namespace Fonte.Api.Indexing;

/// <summary>
/// Coordena leitura, chunking, embeddings e publicação no Qdrant. Uma indexação por vez
/// nesta instância da aplicação; uma chamada simultânea recebe <see cref="DocumentIndexingOutcome.AlreadyRunning"/>.
/// </summary>
public sealed partial class DocumentIndexer(
    MarkdownDocumentReader reader,
    MarkdownChunker chunker,
    Func<ChunkEmbedder> embedderFactory,
    Func<ChunkVectorStore> vectorStoreFactory,
    IndexingMetrics metrics,
    TimeProvider timeProvider,
    ILogger<DocumentIndexer> logger)
{
    private readonly SemaphoreSlim _running = new(1, 1);

    public async Task<DocumentIndexingOutcome> IndexAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = timeProvider.GetTimestamp();

        if (!await _running.WaitAsync(0, cancellationToken))
        {
            LogAlreadyRunning();
            metrics.RecordAttempt(timeProvider.GetElapsedTime(startedAt), IndexingMetrics.Outcomes.AlreadyRunning);
            return DocumentIndexingOutcome.AlreadyRunning;
        }

        try
        {
            return await RunAsync(startedAt, cancellationToken);
        }
        finally
        {
            _running.Release();
        }
    }

    private async Task<DocumentIndexingOutcome> RunAsync(long startedAt, CancellationToken cancellationToken)
    {
        LogStarted();
        var stage = Stages.Configuration;

        try
        {
            // Resolvidos aqui, e não no construtor: a falta de configuração do Gemini ou do Qdrant
            // é uma falha desta indexação, antes de ler documentos ou chamar serviços externos.
            var embedder = embedderFactory();
            var vectorStore = vectorStoreFactory();

            stage = Stages.Read;
            var documents = await TraceAsync(FonteTelemetry.Spans.DocumentsRead, async activity =>
            {
                var read = await reader.ReadAllAsync(cancellationToken);
                activity?.SetTag(FonteTelemetry.Attributes.DocumentsCount, read.Count);
                return read;
            });

            if (documents.Count == 0)
            {
                LogNoDocuments();
                metrics.RecordAttempt(timeProvider.GetElapsedTime(startedAt), IndexingMetrics.Outcomes.NoDocuments);
                return DocumentIndexingOutcome.NoDocuments;
            }

            stage = Stages.Chunk;
            var chunks = await TraceAsync(FonteTelemetry.Spans.DocumentsChunk, activity =>
            {
                IReadOnlyList<DocumentChunk> chunked = documents.SelectMany(chunker.Chunk).ToList();
                activity?.SetTag(FonteTelemetry.Attributes.ChunksCount, chunked.Count);
                return Task.FromResult(chunked);
            });

            stage = Stages.Embedding;
            var embedded = await TraceAsync(FonteTelemetry.Spans.EmbeddingCreate, _ =>
                embedder.EmbedAsync(chunks, cancellationToken));

            stage = Stages.VectorStore;
            var cleanupCompleted = await TraceAsync(FonteTelemetry.Spans.VectorStoreReplace, async activity =>
            {
                var completed = await ReplaceAllAsync(vectorStore, embedded, cancellationToken);
                activity?.SetTag(FonteTelemetry.Attributes.CleanupCompleted, completed);
                return completed;
            });

            var elapsed = timeProvider.GetElapsedTime(startedAt);
            metrics.RecordPublished(documents.Count, chunks.Count);
            metrics.RecordAttempt(
                elapsed,
                cleanupCompleted ? IndexingMetrics.Outcomes.Published : IndexingMetrics.Outcomes.PublishedCleanupFailed);
            LogCompleted(documents.Count, chunks.Count, (long)elapsed.TotalMilliseconds);

            return DocumentIndexingOutcome.Published(documents.Count, chunks.Count, cleanupCompleted);
        }
        catch (Exception exception)
        {
            LogFailed(stage, exception);
            metrics.RecordAttempt(timeProvider.GetElapsedTime(startedAt), IndexingMetrics.Outcomes.Failed, exception);
            throw;
        }
    }

    /// <returns><see langword="false"/> quando a nova indexação foi publicada, mas a limpeza falhou.</returns>
    private async Task<bool> ReplaceAllAsync(
        ChunkVectorStore vectorStore,
        IReadOnlyList<EmbeddedChunk> embedded,
        CancellationToken cancellationToken)
    {
        try
        {
            await vectorStore.ReplaceAllAsync(embedded, cancellationToken);
            return true;
        }
        catch (VectorStoreCleanupException exception)
        {
            LogCleanupFailed(exception.ActiveCollection, exception.FailedCollection, exception);
            return false;
        }
    }

    private static async Task<T> TraceAsync<T>(string spanName, Func<Activity?, Task<T>> operation)
    {
        using var activity = FonteTelemetry.ActivitySource.StartActivity(spanName);

        try
        {
            return await operation(activity);
        }
        catch (Exception exception)
        {
            // Só o tipo: a mensagem pode citar o documento, que fica apenas nos logs.
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag(FonteTelemetry.Attributes.ErrorType, exception.GetType().FullName);
            throw;
        }
    }

    private static class Stages
    {
        public const string Configuration = "configuration";
        public const string Read = "read";
        public const string Chunk = "chunk";
        public const string Embedding = "embedding";
        public const string VectorStore = "vectorstore";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexação iniciada.")]
    private partial void LogStarted();

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexação publicada: {Documents} documentos, {Chunks} chunks, em {ElapsedMs} ms.")]
    private partial void LogCompleted(int documents, int chunks, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Indexação rejeitada: já existe uma indexação em andamento.")]
    private partial void LogAlreadyRunning();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Indexação rejeitada: nenhum documento Markdown na pasta configurada. O índice ativo não foi alterado.")]
    private partial void LogNoDocuments();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Indexação publicada em {ActiveCollection}, mas a limpeza falhou (collection {FailedCollection}). As sobras serão removidas na próxima indexação.")]
    private partial void LogCleanupFailed(string activeCollection, string? failedCollection, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha na indexação na etapa {Stage}. O índice ativo não foi alterado.")]
    private partial void LogFailed(string stage, Exception exception);
}
