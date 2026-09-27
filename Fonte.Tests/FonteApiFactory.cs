using Fonte.Api.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Fonte.Tests;

/// <summary>
/// Factory dos testes de integração. Sobe a aplicação no ambiente <c>Testing</c>, que não carrega o
/// <c>appsettings.Development.json</c> local: testes automatizados não consomem credenciais locais,
/// não chamam Gemini nem Qdrant reais e não exportam telemetria para o backend real.
/// </summary>
public sealed class FonteApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment(OpenTelemetryRegistration.TestingEnvironment);
}
