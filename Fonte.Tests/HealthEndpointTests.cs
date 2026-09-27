using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Fonte.Tests;

public class HealthEndpointTests : IClassFixture<FonteApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(FonteApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealthReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
