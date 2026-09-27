using Fonte.Api.Observability;
using Fonte.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fonte.Tests;

/// <summary>
/// Testes automatizados não consomem credenciais locais nem exportam telemetria para o backend real.
/// </summary>
public class TestingEnvironmentTests(FonteApiFactory factory) : IClassFixture<FonteApiFactory>
{
    [Fact]
    public void IntegrationTestsRunInTestingEnvironment()
    {
        var environment = factory.Services.GetRequiredService<IHostEnvironment>();

        Assert.Equal(OpenTelemetryRegistration.TestingEnvironment, environment.EnvironmentName);
    }

    [Fact]
    public void IntegrationTestsDoNotLoadLocalDevelopmentSettings()
    {
        var configuration = (IConfigurationRoot)factory.Services.GetRequiredService<IConfiguration>();

        var jsonFiles = configuration.Providers
            .OfType<JsonConfigurationProvider>()
            .Select(provider => provider.Source.Path)
            .ToList();

        Assert.Contains("appsettings.json", jsonFiles);
        Assert.DoesNotContain(jsonFiles, path => path is not null && path.Contains("Development", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Development", "https://otlp.exemplo/otlp", true)]
    [InlineData("Production", "https://otlp.exemplo/otlp", true)]
    [InlineData("Development", null, false)]
    [InlineData("Development", "   ", false)]
    [InlineData("Testing", "https://otlp.exemplo/otlp", false)]
    public void OtlpExportIsEnabledOnlyWithEndpointAndOutsideTesting(string environmentName, string? endpoint, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint })
            .Build();
        var environment = new StubHostEnvironment(Path.GetTempPath()) { EnvironmentName = environmentName };

        Assert.Equal(expected, OpenTelemetryRegistration.IsOtlpExportEnabled(configuration, environment));
    }
}
