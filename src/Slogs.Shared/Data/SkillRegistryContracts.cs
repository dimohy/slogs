namespace Slogs.Data;

public sealed record PublicSkillSummary(
    string Slug,
    string Version,
    string Description,
    string ContentHash);
