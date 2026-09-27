using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Fonte.Api.Observability;

public sealed class AnswerMetrics
{
    public static class Outcomes
    {
        public const string Answered = "answered";
        public const string InsufficientContext = "insufficient_context";
        public const string NoContext = "no_context";
        public const string Failed = "failed";
    }

    private readonly Histogram<double> _duration;

    public AnswerMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(FonteTelemetry.Name);

        _duration = meter.CreateHistogram<double>(
            "fonte.answer.duration", unit: "s", description: "Duração das gerações de resposta.");
    }

    public void RecordAttempt(TimeSpan duration, string outcome, Exception? error = null)
    {
        var tags = new TagList { { FonteTelemetry.Attributes.AnswerOutcome, outcome } };

        if (error is not null)
        {
            tags.Add(FonteTelemetry.Attributes.ErrorType, error.GetType().FullName);
        }

        _duration.Record(duration.TotalSeconds, tags);
    }
}
