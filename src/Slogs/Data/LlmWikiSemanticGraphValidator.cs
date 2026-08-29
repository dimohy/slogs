namespace Slogs.Data;

public static class LlmWikiSemanticGraphValidator
{
    public static IReadOnlyList<string> Validate(
        LlmWikiSemanticGraphManifest manifest,
        IReadOnlyDictionary<Guid, LlmWikiSemanticCorpusEntry> entries,
        IReadOnlyDictionary<Guid, LlmWikiSemanticCorpusSource> sources,
        string expectedCorpusSha256)
    {
        var errors = new List<string>();
        if (manifest.SchemaVersion != LlmWikiSemanticGraphContract.SchemaVersion)
        {
            errors.Add($"Unsupported schemaVersion {manifest.SchemaVersion}.");
        }
        if (!string.Equals(manifest.CorpusSha256, expectedCorpusSha256, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("The semantic manifest does not match the frozen corpus SHA-256.");
        }
        if (string.IsNullOrWhiteSpace(manifest.OwnerUserName))
        {
            errors.Add("ownerUserName is required.");
        }

        var entities = new Dictionary<string, LlmWikiSemanticEntity>(StringComparer.Ordinal);
        foreach (var entity in manifest.Entities)
        {
            if (string.IsNullOrWhiteSpace(entity.Key) || !entities.TryAdd(entity.Key, entity))
            {
                errors.Add($"Entity key is empty or duplicated: '{entity.Key}'.");
            }
            if (!LlmWikiSemanticGraphContract.EntityTypes.Contains(entity.EntityType))
            {
                errors.Add($"Unknown entity type '{entity.EntityType}' for '{entity.Key}'.");
            }
        }

        foreach (var mention in manifest.Mentions)
        {
            if (!entities.ContainsKey(mention.EntityKey))
            {
                errors.Add($"Mention references unknown entity '{mention.EntityKey}'.");
            }
            ValidateConfidence(mention.Confidence, $"mention '{mention.EntityKey}'", errors);
            ValidateEvidence(
                new LlmWikiSemanticEvidence(mention.EntryId, mention.SourceId, mention.EvidenceField, mention.EvidenceQuote),
                manifest.OwnerUserName,
                entries,
                sources,
                errors);
        }

        var relationKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relation in manifest.Relations)
        {
            if (!entities.ContainsKey(relation.FromEntityKey) || !entities.ContainsKey(relation.ToEntityKey))
            {
                errors.Add($"Relation '{relation.RelationType}' has an unknown endpoint.");
            }
            if (relation.FromEntityKey == relation.ToEntityKey)
            {
                errors.Add($"Relation '{relation.RelationType}' cannot point to the same entity.");
            }
            if (!LlmWikiSemanticGraphContract.RelationTypes.Contains(relation.RelationType))
            {
                errors.Add($"Unknown relation type '{relation.RelationType}'.");
            }
            if (!relationKeys.Add($"{relation.FromEntityKey}\u001f{relation.RelationType}\u001f{relation.ToEntityKey}"))
            {
                errors.Add($"Duplicate relation '{relation.FromEntityKey}' -> '{relation.ToEntityKey}' ({relation.RelationType}).");
            }
            ValidateConfidence(relation.Confidence, $"relation '{relation.RelationType}'", errors);
            if (relation.Evidence.Count == 0)
            {
                errors.Add($"Relation '{relation.RelationType}' requires evidence.");
            }
            foreach (var evidence in relation.Evidence)
            {
                ValidateEvidence(evidence, manifest.OwnerUserName, entries, sources, errors);
            }
        }

        foreach (var split in manifest.SplitProposals)
        {
            if (!entries.TryGetValue(split.SourceEntryId, out var sourceEntry) || sourceEntry.OwnerUserName != manifest.OwnerUserName)
            {
                errors.Add($"Split proposal references an unknown or cross-owner entry '{split.SourceEntryId}'.");
            }
            if (string.IsNullOrWhiteSpace(split.ProposedTitle) || string.IsNullOrWhiteSpace(split.ProposedPrompt))
            {
                errors.Add($"Split proposal for '{split.SourceEntryId}' requires a title and prompt.");
            }
            if (split.Evidence.Count == 0)
            {
                errors.Add($"Split proposal for '{split.SourceEntryId}' requires evidence.");
            }
            foreach (var evidence in split.Evidence)
            {
                if (evidence.EntryId != split.SourceEntryId)
                {
                    errors.Add($"Split evidence must belong to source entry '{split.SourceEntryId}'.");
                }
                ValidateEvidence(evidence, manifest.OwnerUserName, entries, sources, errors);
            }
        }

        return errors;
    }

    private static void ValidateConfidence(double confidence, string target, ICollection<string> errors)
    {
        if (!double.IsFinite(confidence) || confidence is < 0.0 or > 1.0)
        {
            errors.Add($"Confidence for {target} must be between 0 and 1.");
        }
    }

    private static void ValidateEvidence(
        LlmWikiSemanticEvidence evidence,
        string owner,
        IReadOnlyDictionary<Guid, LlmWikiSemanticCorpusEntry> entries,
        IReadOnlyDictionary<Guid, LlmWikiSemanticCorpusSource> sources,
        ICollection<string> errors)
    {
        if (!LlmWikiSemanticGraphContract.EvidenceFields.Contains(evidence.EvidenceField))
        {
            errors.Add($"Unknown evidence field '{evidence.EvidenceField}'.");
            return;
        }
        if (string.IsNullOrWhiteSpace(evidence.EvidenceQuote))
        {
            errors.Add("Evidence quote must not be empty.");
            return;
        }
        if (!entries.TryGetValue(evidence.EntryId, out var entry) || entry.OwnerUserName != owner)
        {
            errors.Add($"Evidence references unknown or cross-owner entry '{evidence.EntryId}'.");
            return;
        }

        string? sourceText = evidence.EvidenceField switch
        {
            "title" when evidence.SourceId is null => entry.Title,
            "summary" when evidence.SourceId is null => entry.Summary,
            "category-path" when evidence.SourceId is null => entry.CategoryPath,
            "source-prompt" when evidence.SourceId is null => entry.SourcePrompt,
            "content" when evidence.SourceId is null => entry.Content,
            "raw-prompt" when evidence.SourceId is not null && sources.TryGetValue(evidence.SourceId.Value, out var source)
                && source.EntryId == entry.Id && source.OwnerUserName == owner => source.Prompt,
            "raw-content" when evidence.SourceId is not null && sources.TryGetValue(evidence.SourceId.Value, out var source)
                && source.EntryId == entry.Id && source.OwnerUserName == owner => source.Content,
            _ => null
        };
        if (sourceText is null || !sourceText.Contains(evidence.EvidenceQuote, StringComparison.Ordinal))
        {
            errors.Add($"Evidence quote was not found in {evidence.EvidenceField} for entry '{evidence.EntryId}'.");
        }
    }
}

public sealed record LlmWikiSemanticCorpusEntry(
    Guid Id,
    string OwnerUserName,
    string Title,
    string Summary,
    string CategoryPath,
    string SourcePrompt,
    string Content);
public sealed record LlmWikiSemanticCorpusSource(Guid Id, Guid EntryId, string OwnerUserName, string Prompt, string? Content);
