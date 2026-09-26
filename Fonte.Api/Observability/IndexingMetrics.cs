using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Fonte.Api.Observability;

public sealed class IndexingMetrics
{
    public static class Outcomes
    {
        public const string Published = "published";
        public const string PublishedCleanupFailed = "published_cleanup_failed";
        public const string Failed = "failed";
        public const string AlreadyRunning = "already_running";
        public const string NoDocuments = "no_documents";
    }

    private readonly Histogram<double> _duration;
    private readonly Counter<long> _documents;
    private readonly Counter<long> _chunks;

    public IndexingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(FonteTelemetry.Name);

        _duration = meter.CreateHistogram<double>(
            "fonte.indexing.duration", unit: "s", description: "Duração das tentativas de indexação.");
        _documents = meter.CreateCounter<long>(
            "fonte.indexing.documents", unit: "{document}", description: "Documentos publicados por indexações bem-sucedidas.");
        _chunks = meter.CreateCounter<long>(
            "fonte.indexing.chunks", unit: "{chunk}", description: "Chunks publicados por indexações bem-sucedidas.");
    }

    public void RecordPublished(int documents, int chunks)
    {
        _documents.Add(documents);
        _chunks.Add(chunks);
    }

    public void RecordAttempt(TimeSpan duration, string outcome, Exception? error = null)
    {
        var tags = new TagList { { FonteTelemetry.Attributes.IndexingOutcome, outcome } };

        if (error is not null)
        {
            tags.Add(FonteTelemetry.Attributes.ErrorType, error.GetType().FullName);
        }

        _duration.Record(duration.TotalSeconds, tags);
    }
}
