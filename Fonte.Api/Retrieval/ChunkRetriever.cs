using System.Diagnostics;
using Fonte.Api.Embeddings;
using Fonte.Api.Observability;
using Fonte.Api.VectorStore;
using Microsoft.Extensions.Options;

namespace Fonte.Api.Retrieval;

/// <summary>
/// Recupera os chunks mais similares a uma pergunta: embedding da pergunta e busca Top N
/// na indexação ativa. Índice sem resultados retorna lista vazia; alias inexistente é falha.
/// </summary>
public sealed partial class ChunkRetriever(
    Func<QueryEmbedder> embedderFactory,
    Func<ChunkVectorStore> vectorStoreFactory,
    IOptions<RetrievalOptions> options,
    RetrievalMetrics metrics,
    TimeProvider timeProvider,
    ILogger<ChunkRetriever> logger)
{
    private readonly int _topK = options.Value.TopK;

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var startedAt = timeProvider.GetTimestamp();
        var stage = Stages.Configuration;

        try
        {
            // Resolvidos aqui, e não no construtor: a falta de configuração é uma falha desta operação.
            var embedder = embedderFactory();
            var vectorStore = vectorStoreFactory();

            stage = Stages.Embedding;
            var vector = await TraceAsync(FonteTelemetry.Spans.EmbeddingCreate, _ =>
                embedder.EmbedAsync(question, cancellationToken));

            stage = Stages.Search;
            var results = await TraceAsync(FonteTelemetry.Spans.RetrievalSearch, async activity =>
            {
                activity?.SetTag(FonteTelemetry.Attributes.RetrievalTopK, _topK);
                var found = await vectorStore.SearchAsync(vector, _topK, cancellationToken);
                activity?.SetTag(FonteTelemetry.Attributes.RetrievalResults, found.Count);
                return found;
            });

            var elapsed = timeProvider.GetElapsedTime(startedAt);
            metrics.RecordAttempt(elapsed, RetrievalMetrics.Outcomes.Success);
            LogRetrieved(results.Count, (long)elapsed.TotalMilliseconds);

            return results;
        }
        catch (Exception exception)
        {
            LogFailed(stage, exception);
            metrics.RecordAttempt(timeProvider.GetElapsedTime(startedAt), RetrievalMetrics.Outcomes.Failed, exception);
            throw;
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
            // Só o tipo: a mensagem pode conter dados que não devem ir para o trace.
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag(FonteTelemetry.Attributes.ErrorType, exception.GetType().FullName);
            throw;
        }
    }

    private static class Stages
    {
        public const string Configuration = "configuration";
        public const string Embedding = "embedding";
        public const string Search = "search";
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recuperação concluída: {Results} chunks em {ElapsedMs} ms.")]
    private partial void LogRetrieved(int results, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha na recuperação na etapa {Stage}.")]
    private partial void LogFailed(string stage, Exception exception);
}
