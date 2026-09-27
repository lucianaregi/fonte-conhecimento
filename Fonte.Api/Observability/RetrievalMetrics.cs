using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Fonte.Api.Observability;

public sealed class RetrievalMetrics
{
    public static class Outcomes
    {
        public const string Success = "success";
        public const string Failed = "failed";
    }

    private readonly Histogram<double> _duration;

    public RetrievalMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(FonteTelemetry.Name);

        _duration = meter.CreateHistogram<double>(
            "fonte.retrieval.duration", unit: "s", description: "Duração das recuperações de chunks para perguntas.");
    }

    public void RecordAttempt(TimeSpan duration, string outcome, Exception? error = null)
    {
        var tags = new TagList { { FonteTelemetry.Attributes.RetrievalOutcome, outcome } };

        if (error is not null)
        {
            tags.Add(FonteTelemetry.Attributes.ErrorType, error.GetType().FullName);
        }

        _duration.Record(duration.TotalSeconds, tags);
    }
}
