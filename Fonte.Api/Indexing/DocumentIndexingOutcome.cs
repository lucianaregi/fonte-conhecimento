namespace Fonte.Api.Indexing;

public enum DocumentIndexingStatus
{
    Published,
    AlreadyRunning,
    NoDocuments,
}

public sealed record DocumentIndexingOutcome(
    DocumentIndexingStatus Status,
    int Documents = 0,
    int Chunks = 0,
    bool CleanupCompleted = false)
{
    public static readonly DocumentIndexingOutcome AlreadyRunning = new(DocumentIndexingStatus.AlreadyRunning);

    public static readonly DocumentIndexingOutcome NoDocuments = new(DocumentIndexingStatus.NoDocuments);

    public static DocumentIndexingOutcome Published(int documents, int chunks, bool cleanupCompleted) =>
        new(DocumentIndexingStatus.Published, documents, chunks, cleanupCompleted);
}
