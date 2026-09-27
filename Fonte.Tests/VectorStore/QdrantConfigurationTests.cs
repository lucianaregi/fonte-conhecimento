using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;

namespace Fonte.Tests.VectorStore;

public class QdrantConfigurationTests(FonteApiFactory factory) : IClassFixture<FonteApiFactory>
{
    [Theory]
    [InlineData("", "chave", "Qdrant:Url")]
    [InlineData("https://exemplo.cloud.qdrant.io:6334", "", "Qdrant:ApiKey")]
    public void ClientRequiresUrlAndApiKey(string url, string apiKey, string missingKey)
    {
        var configured = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Qdrant:Url", url)
            .UseSetting("Qdrant:ApiKey", apiKey));

        var exception = Assert.Throws<InvalidOperationException>(
            () => configured.Services.GetRequiredService<QdrantClient>());
        Assert.Contains(missingKey, exception.Message);
    }
}
