using Microsoft.Extensions.AI;

namespace Fonte.Tests.Fakes;

public sealed class FakeEmbeddingGenerator(int dimensions) : IEmbeddingGenerator<string, Embedding<float>>
{
    private int _generated;

    public int EmbeddingsPerCall { get; init; } = 1;

    /// <summary>Quando definido, toda chamada falha com esta exceção.</summary>
    public Exception? Failure { get; init; }

    /// <summary>Quando definido, cada chamada aguarda esta tarefa antes de responder.</summary>
    public TaskCompletionSource? Gate { get; init; }

    /// <summary>Concluída quando a primeira chamada começa.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<string[]> Calls { get; } = [];

    public List<CancellationToken> CancellationTokens { get; } = [];

    public static float[] VectorFor(int sequence, int dimensions) =>
        Enumerable.Repeat((float)sequence, dimensions).ToArray();

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(values.ToArray());
        CancellationTokens.Add(cancellationToken);
        Entered.TrySetResult();

        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(cancellationToken);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        var embeddings = new GeneratedEmbeddings<Embedding<float>>();
        for (var i = 0; i < EmbeddingsPerCall; i++)
        {
            embeddings.Add(new Embedding<float>(VectorFor(_generated++, dimensions)));
        }

        return embeddings;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
