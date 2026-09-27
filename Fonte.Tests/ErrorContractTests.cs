using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fonte.Api.VectorStore;
using Fonte.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fonte.Tests;

/// <summary>
/// Contrato de erro da aplicação: igual em Development, Testing e produção, com <c>traceId</c>
/// para correlação com o trace e sem expor dados da exceção.
/// </summary>
public sealed class ErrorContractTests(FonteApiFactory factory) : IClassFixture<FonteApiFactory>, IDisposable
{
    private const string Question = "Qual é a função do ActivitySource?";
    private const string SecretMessage = "segredo-AIzaXYZ123 falhou ao processar 'Qual é a função do ActivitySource?'";

    private readonly string _emptyContentRoot = Directory.CreateTempSubdirectory("fonte-tests-").FullName;

    public void Dispose() => Directory.Delete(_emptyContentRoot, recursive: true);

    /// <summary>Aplicação cujo embedding sempre falha, para provocar um 500 em <c>POST /questions</c>.</summary>
    private WebApplicationFactory<Program> CreateFailingApp(int dimensions, Action<IWebHostBuilder>? configure = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            configure?.Invoke(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                    new FakeEmbeddingGenerator(dimensions) { Failure = new HttpRequestException(SecretMessage) });
                services.AddSingleton<IQdrantGateway>(new FakeQdrantGateway());
            });
        });

    /// <summary>
    /// Development com a pasta de conteúdo vazia: nenhum appsettings é carregado, inclusive o
    /// <c>appsettings.Development.json</c> local com credenciais.
    /// </summary>
    private WebApplicationFactory<Program> CreateFailingDevelopmentApp() =>
        CreateFailingApp(dimensions: 768, builder => builder
            .UseEnvironment(Environments.Development)
            .UseContentRoot(_emptyContentRoot));

    private static HttpRequestMessage QuestionRequest(string? traceparent = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/questions") { Content = JsonContent.Create(new { question = Question }) };
        if (traceparent is not null)
        {
            request.Headers.Add("traceparent", traceparent);
        }

        return request;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static void AssertInternalServerErrorContract(JsonElement problem)
    {
        Assert.Equal(["type", "title", "status", "detail", "traceId"], problem.EnumerateObject().Select(p => p.Name));
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.6.1", problem.GetProperty("type").GetString());
        Assert.Equal("Erro interno do servidor", problem.GetProperty("title").GetString());
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.Equal("Ocorreu um erro inesperado ao processar a requisição.", problem.GetProperty("detail").GetString());
        Assert.Matches("^[0-9a-f]{32}$", problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task UnhandledExceptionReturnsFixedInternalServerErrorProblem()
    {
        using var app = CreateFailingApp(dimensions: 768);

        var response = await app.CreateClient().SendAsync(QuestionRequest());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertInternalServerErrorContract(await ReadProblemAsync(response));
    }

    [Fact]
    public async Task TraceIdIsTheTraceOfTheRequest()
    {
        var traceId = ActivityTraceId.CreateRandom();
        using var app = CreateFailingApp(dimensions: 768);

        var response = await app.CreateClient().SendAsync(QuestionRequest($"00-{traceId}-{ActivitySpanId.CreateRandom()}-01"));

        var problem = await ReadProblemAsync(response);
        Assert.Equal(traceId.ToHexString(), problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task InternalServerErrorExposesNothingFromTheException()
    {
        using var app = CreateFailingApp(dimensions: 768);

        var response = await app.CreateClient().SendAsync(QuestionRequest());

        var body = await response.Content.ReadAsStringAsync();
        string[] forbidden = ["segredo", "AIza", Question, "ActivitySource", nameof(HttpRequestException), "   at ", "Fonte.", "StackTrace", "exception"];
        Assert.All(forbidden, f => Assert.DoesNotContain(f, body, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DevelopmentReturnsTheSameInternalServerErrorContract()
    {
        using var app = CreateFailingDevelopmentApp();
        Assert.Equal(Environments.Development, app.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        Assert.False(File.Exists(Path.Combine(_emptyContentRoot, "appsettings.Development.json")));

        var response = await app.CreateClient().SendAsync(QuestionRequest());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertInternalServerErrorContract(await ReadProblemAsync(response));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ isto não é json")]
    public async Task MalformedBodyReturnsBadRequestProblem(string content)
    {
        using var app = factory.WithWebHostBuilder(_ => { });

        var response = await app.CreateClient().PostAsync("/questions", new StringContent(content, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("Requisição inválida", problem.GetProperty("title").GetString());
        Assert.Matches("^[0-9a-f]{32}$", problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task MalformedBodyInDevelopmentIsStillBadRequestProblem()
    {
        using var app = CreateFailingDevelopmentApp();

        var response = await app.CreateClient().PostAsync("/questions", new StringContent("{ isto não é json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Requisição inválida", (await ReadProblemAsync(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task UnknownRouteReturnsNotFoundProblem()
    {
        var response = await factory.CreateClient().GetAsync("/rota-inexistente");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal("Recurso não encontrado", problem.GetProperty("title").GetString());
        Assert.Matches("^[0-9a-f]{32}$", problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task WrongMethodReturnsMethodNotAllowedProblem()
    {
        var response = await factory.CreateClient().GetAsync("/questions");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(405, problem.GetProperty("status").GetInt32());
        Assert.Equal("Método não permitido", problem.GetProperty("title").GetString());
        Assert.Matches("^[0-9a-f]{32}$", problem.GetProperty("traceId").GetString());
    }
}
