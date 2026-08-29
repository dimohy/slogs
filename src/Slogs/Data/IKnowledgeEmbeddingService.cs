namespace Slogs.Data;

public interface IKnowledgeEmbeddingService
{
    string Model { get; }
    int Dimensions { get; }
    bool SupportsFullFunctionReranking { get; }
    Task<IReadOnlyList<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken);
    Task<IReadOnlyList<float>> EmbedDocumentAsync(string document, CancellationToken cancellationToken);
    Task<IReadOnlyList<IReadOnlyList<float>>> EmbedDocumentsAsync(
        IReadOnlyList<string> documents,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeRerankScore>> ScorePairsAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken);
}

public sealed record KnowledgeRerankScore(float Dense, float Sparse, float MultiVector, float Combined);
