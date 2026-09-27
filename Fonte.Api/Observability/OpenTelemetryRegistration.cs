using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Fonte.Api.Observability;

public static class OpenTelemetryRegistration
{
    private const string ServiceName = "fonte";

    /// <summary>Ambiente dos testes de integração.</summary>
    public const string TestingEnvironment = "Testing";

    /// <summary>
    /// Testes automatizados nunca exportam para o backend real, mesmo que o endpoint venha de uma
    /// variável de ambiente da máquina.
    /// </summary>
    public static bool IsOtlpExportEnabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsEnvironment(TestingEnvironment) &&
        !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

    /// <summary>
    /// Coleta logs, traces e métricas. A exportação OTLP só é ligada quando
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> está configurado e fora do ambiente <c>Testing</c>; endpoint,
    /// protocolo e cabeçalhos (credenciais) vêm das chaves <c>OTEL_EXPORTER_OTLP_*</c>, nunca do repositório.
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
                .AddSource("System.Net.Http")
                // Span nativo do Grpc.Net.Client (Qdrant): sem ele, a Activity gRPC fica fora do trace e
                // o span HTTP do Qdrant perde o pai exportado.
                .AddSource("Grpc.Net.Client"))
            .WithMetrics(metrics => metrics
                .AddMeter(FonteTelemetry.Name)
                .AddAspNetCoreInstrumentation()
                .AddMeter("System.Net.Http"))
            .WithLogging(logging => { }, options => options.IncludeFormattedMessage = true);

        if (IsOtlpExportEnabled(builder.Configuration, builder.Environment))
        {
            openTelemetry.UseOtlpExporter();
        }

        return builder;
    }
}
