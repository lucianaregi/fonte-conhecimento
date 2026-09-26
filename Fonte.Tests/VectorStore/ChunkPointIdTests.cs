using Fonte.Api.VectorStore;

namespace Fonte.Tests.VectorStore;

public class ChunkPointIdTests
{
    [Fact]
    public void CreateVersion5MatchesRfc9562Example()
    {
        var dnsNamespace = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        var id = ChunkPointId.CreateVersion5(dnsNamespace, "www.example.com");

        Assert.Equal(Guid.Parse("2ed6657d-e927-568b-95e1-2665a8aea6a2"), id);
    }

    [Fact]
    public void SameChunkAlwaysProducesSameId()
    {
        Assert.Equal(ChunkPointId.For("guias/dotnet.md", 3), ChunkPointId.For("guias/dotnet.md", 3));
    }

    [Theory]
    [InlineData("a.md", 0, "b.md", 0)]
    [InlineData("a.md", 0, "a.md", 1)]
    [InlineData("a.md", 10, "a.md", 1)]
    public void DifferentChunksProduceDifferentIds(string firstPath, int firstIndex, string secondPath, int secondIndex)
    {
        Assert.NotEqual(ChunkPointId.For(firstPath, firstIndex), ChunkPointId.For(secondPath, secondIndex));
    }

    [Fact]
    public void IdIsVersion5()
    {
        Assert.Equal(5, ChunkPointId.For("a.md", 0).Version);
    }
}
