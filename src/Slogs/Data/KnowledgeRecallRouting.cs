namespace Slogs.Data;

internal static class KnowledgeRecallRouting
{
    private static readonly string[] CorpusIntentTerms =
    [
        "성경", "본문", "구절", "원문", "근거", "출처", "문서", "매뉴얼", "사양", "규정", "논문", "책",
        "corpus", "evidence", "source", "document", "manual", "specification", "paper", "book", "verse"
    ];

    public static bool ShouldUseFullFunctionReranking(
        int maxGraphHops,
        bool requested,
        bool supported)
        => maxGraphHops > 1 && requested && supported;

    public static string GetProfile(int maxGraphHops)
        => maxGraphHops > 1 ? "relational-bge-m3-full" : "general-bge-m3-dense";

    public static bool ShouldSearchKnowledgeCorpus(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (KnowledgeCorpusService.HasSingleExplicitLocatorQuery(query))
        {
            return true;
        }

        var normalized = query.Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant();
        return CorpusIntentTerms.Any(term => normalized.Contains(term, StringComparison.Ordinal));
    }
}
