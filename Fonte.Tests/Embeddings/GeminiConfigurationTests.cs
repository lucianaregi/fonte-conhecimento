using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Fonte.Tests.Embeddings;

public class GeminiConfigurationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("Gemini:EmbeddingDimensions", "127")]
    [InlineData("Gemini:EmbeddingDimensions", "3073")]
    [InlineData("Gemini:EmbeddingModel", "")]
    public void InvalidConfigurationPreventsStartup(string key, string value)
    {
        var invalid = factory.WithWebHostBuilder(builder => builder.UseSetting(key, value));

        Assert.Throws<OptionsValidationException>(() => invalid.CreateClient());
    }

    [Fact]
    public void EmbeddingGeneratorRequiresApiKey()
    {
        var withoutKey = factory.WithWebHostBuilder(builder => builder.UseSetting("Gemini:ApiKey", ""));

        var exception = Assert.Throws<InvalidOperationException>(
            () => withoutKey.Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>());
        Assert.Contains("Gemini:ApiKey", exception.Message);
    }
}
