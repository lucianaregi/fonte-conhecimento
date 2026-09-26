using System.Security.Cryptography;
using System.Text;

namespace Fonte.Api.VectorStore;

/// <summary>
/// ID determinístico do ponto no Qdrant: UUID v5 (RFC 9562) derivado de documento e índice do chunk.
/// </summary>
public static class ChunkPointId
{
    private static readonly Guid FonteNamespace = Guid.Parse("4f0c5a1e-8b2d-4e7a-9c3f-6d1b2a7e5c90");

    // O índice vem antes do caminho: como não contém ':', o nome é inequívoco.
    public static Guid For(string documentPath, int index) =>
        CreateVersion5(FonteNamespace, $"{index}:{documentPath}");

    public static Guid CreateVersion5(Guid namespaceId, string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var data = new byte[16 + nameBytes.Length];
        namespaceId.TryWriteBytes(data, bigEndian: true, out _);
        nameBytes.CopyTo(data, 16);

        var hash = SHA1.HashData(data);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
