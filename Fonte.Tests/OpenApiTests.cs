using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Fonte.Tests;

/// <summary>
/// Especificação OpenAPI e Swagger UI: disponíveis apenas em Development e descrevendo os contratos
/// atuais dos endpoints, inclusive os <c>ProblemDetails</c> com <c>traceId</c>.
/// </summary>
public sealed class OpenApiTests(FonteApiFactory factory) : IClassFixture<FonteApiFactory>, IDisposable
{
    private const string DocumentPath = "/openapi/v1.json";
    private const string ProblemJson = "application/problem+json";

    private readonly string _emptyContentRoot = Directory.CreateTempSubdirectory("fonte-tests-").FullName;

    public void Dispose() => Directory.Delete(_emptyContentRoot, recursive: true);

    /// <summary>
    /// Development com a pasta de conteúdo vazia: nenhum appsettings é carregado, inclusive o
    /// <c>appsettings.Development.json</c> local com credenciais.
    /// </summary>
    private WebApplicationFactory<Program> CreateDevelopmentApp() =>
        factory.WithWebHostBuilder(builder => builder
            .UseEnvironment(Environments.Development)
            .UseContentRoot(_emptyContentRoot));

    private async Task<JsonElement> GetDocumentAsync()
    {
        using var app = CreateDevelopmentApp();
        var response = await app.CreateClient().GetAsync(DocumentPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement Resolve(JsonElement document, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        var name = reference.GetString()!.Split('/')[^1];
        return document.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }

    private static JsonElement ResponseSchema(JsonElement document, JsonElement operation, string status, string mediaType) =>
        Resolve(document, operation.GetProperty("responses").GetProperty(status)
            .GetProperty("content").GetProperty(mediaType).GetProperty("schema"));

    private static string[] PropertyNames(JsonElement schema) =>
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToArray();

    private static string[] ResponseStatuses(JsonElement operation) =>
        operation.GetProperty("responses").EnumerateObject().Select(p => p.Name).Order().ToArray();

    [Fact]
    public async Task DevelopmentExposesOpenApi31DocumentWithAllEndpoints()
    {
        var document = await GetDocumentAsync();

        Assert.StartsWith("3.1", document.GetProperty("openapi").GetString());
        var paths = document.GetProperty("paths");
        Assert.True(paths.GetProperty("/health").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/documents/index").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/questions").TryGetProperty("post", out _));
        Assert.Equal(["/documents/index", "/health", "/questions"], paths.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task HealthIsDocumentedAsOkWithoutBody()
    {
        var document = await GetDocumentAsync();
        var operation = document.GetProperty("paths").GetProperty("/health").GetProperty("get");

        Assert.Equal(["200"], ResponseStatuses(operation));
        Assert.False(operation.GetProperty("responses").GetProperty("200").TryGetProperty("content", out _));
    }

    [Fact]
    public async Task DocumentIndexingDocumentsSuccessAndProblemResponses()
    {
        var document = await GetDocumentAsync();
        var operation = document.GetProperty("paths").GetProperty("/documents/index").GetProperty("post");

        Assert.False(operation.TryGetProperty("requestBody", out _));
        Assert.Equal(["200", "409", "422", "500"], ResponseStatuses(operation));
        Assert.Equal(["chunks", "cleanupCompleted", "documents"], PropertyNames(ResponseSchema(document, operation, "200", "application/json")));

        foreach (var status in new[] { "409", "422", "500" })
        {
            Assert.Contains("traceId", PropertyNames(ResponseSchema(document, operation, status, ProblemJson)));
        }
    }

    [Fact]
    public async Task QuestionsDocumentsRequestSuccessAndProblemResponses()
    {
        var document = await GetDocumentAsync();
        var operation = document.GetProperty("paths").GetProperty("/questions").GetProperty("post");

        var requestBody = operation.GetProperty("requestBody");
        Assert.True(requestBody.GetProperty("required").GetBoolean());
        var request = Resolve(document, requestBody.GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        Assert.Equal(["question"], PropertyNames(request));
        Assert.Equal(["question"], request.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(2000, request.GetProperty("properties").GetProperty("question").GetProperty("maxLength").GetInt32());

        Assert.Equal(["200", "400", "500"], ResponseStatuses(operation));

        var response = ResponseSchema(document, operation, "200", "application/json");
        Assert.Equal(["answer", "sources", "status"], PropertyNames(response));
        var source = Resolve(document, response.GetProperty("properties").GetProperty("sources").GetProperty("items"));
        Assert.Equal(["chunk", "document", "number", "score"], PropertyNames(source));

        var validationProblem = ResponseSchema(document, operation, "400", ProblemJson);
        Assert.Contains("errors", PropertyNames(validationProblem));
        Assert.Contains("traceId", PropertyNames(validationProblem));
        Assert.Contains("traceId", PropertyNames(ResponseSchema(document, operation, "500", ProblemJson)));
    }

    [Fact]
    public async Task DevelopmentServesSwaggerUi()
    {
        using var app = CreateDevelopmentApp();
        var response = await app.CreateClient().GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(DocumentPath)]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    public async Task OutsideDevelopmentOpenApiAndSwaggerAreNotExposed(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }
}
