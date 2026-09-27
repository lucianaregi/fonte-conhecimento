using Fonte.Api.Retrieval;
using Fonte.Api.VectorStore;
using Qdrant.Client.Grpc;

namespace Fonte.Tests.VectorStore;

public class QdrantGatewayMappingTests
{
    [Fact]
    public void ToPointStructMapsIdVectorAndPayload()
    {
        var id = Guid.Parse("2ed6657d-e927-568b-95e1-2665a8aea6a2");
        var point = new ChunkPoint(id, "guias/dotnet.md", 2, "ActivitySource cria spans.", new[] { 0.1f, 0.2f, 0.3f });

        var mapped = QdrantGateway.ToPointStruct(point);

        Assert.Equal(id.ToString(), mapped.Id.Uuid);
        Assert.Equal([0.1f, 0.2f, 0.3f], mapped.Vectors.Vector.Dense.Data);
        Assert.Equal(3, mapped.Payload.Count);
        Assert.Equal("guias/dotnet.md", mapped.Payload["document_path"].StringValue);
        Assert.Equal(2, mapped.Payload["chunk_index"].IntegerValue);
        Assert.Equal("ActivitySource cria spans.", mapped.Payload["content"].StringValue);
    }

    [Fact]
    public void ToRetrievedChunkMapsPayloadAndScore()
    {
        var point = new ScoredPoint
        {
            Score = 0.87f,
            Payload =
            {
                ["document_path"] = "guias/observabilidade.md",
                ["chunk_index"] = 4L,
                ["content"] = "ActivitySource cria spans.",
            },
        };

        var chunk = QdrantGateway.ToRetrievedChunk(point);

        Assert.Equal(new RetrievedChunk("guias/observabilidade.md", 4, "ActivitySource cria spans.", 0.87f), chunk);
    }

    [Theory]
    [InlineData("document_path")]
    [InlineData("chunk_index")]
    [InlineData("content")]
    public void ToRetrievedChunkThrowsWhenPayloadFieldIsMissing(string missingField)
    {
        var point = new ScoredPoint
        {
            Score = 0.5f,
            Payload =
            {
                ["document_path"] = "a.md",
                ["chunk_index"] = 0L,
                ["content"] = "texto",
            },
        };
        point.Payload.Remove(missingField);

        Assert.Throws<InvalidOperationException>(() => QdrantGateway.ToRetrievedChunk(point));
    }

    [Fact]
    public void ToVectorParamsMapsSizeAndCosineDistance()
    {
        var parameters = QdrantGateway.ToVectorParams(768, VectorDistance.Cosine);

        Assert.Equal(768UL, parameters.Size);
        Assert.Equal(Distance.Cosine, parameters.Distance);
    }
}
