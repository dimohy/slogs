namespace Slogs.Data;

internal static class KnowledgeRecallRouting
{
    public static bool ShouldUseFullFunctionReranking(
        int maxGraphHops,
        bool requested,
        bool supported)
        => maxGraphHops > 1 && requested && supported;

    public static string GetProfile(int maxGraphHops)
        => maxGraphHops > 1 ? "relational-bge-m3-full" : "general-bge-m3-dense";
}
