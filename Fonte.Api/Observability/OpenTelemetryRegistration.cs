using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Fonte.Api.Observability;

public static class OpenTelemetryRegistration
{
    private const string ServiceName = "fonte";

    /// <summary>
    /// Coleta logs, traces e métricas. A exportação OTLP só é ligada quando
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> está configurado; endpoint, protocolo e cabeçalhos
    /// (credenciais) vêm das variáveis <c>OTEL_EXPORTER_OTLP_*</c>, nunca do repositório.
    /// </summary>
    public static WebApplicationBuilder AddFonteOpenTelemetry(this WebApplicationBuilder builder)
    {
        var openTelemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddSource(FonteTelemetry.Name)
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                // No .NET 9+, o HttpClient emite spans nativamente; cobre as chamadas ao Gemini e ao Qdrant.
                .AddSource("System.Net.Http"))
            .WithMetrics(metrics => metrics
                .AddMeter(FonteTelemetry.Name)
                .AddAspNetCoreInstrumentation()
                .AddMeter("System.Net.Http"))
            .WithLogging(logging => { }, options => options.IncludeFormattedMessage = true);

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            openTelemetry.UseOtlpExporter();
        }

        return builder;
    }
}
