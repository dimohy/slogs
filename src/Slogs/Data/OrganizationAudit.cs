using System.Text.Json;

namespace Slogs.Data;

public static class OrganizationAudit
{
    public static OrganizationAuditRecord Create(
        OrganizationActorContext actor,
        string action,
        string targetType,
        string? targetId,
        string outcome = "success",
        object? detail = null)
        => new()
        {
            OrganizationId = actor.OrganizationId,
            ActorKind = actor.ActorKind,
            ActorId = actor.ActorId,
            PresenterUserName = actor.PresenterUserName,
            TokenId = actor.TokenId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Outcome = outcome,
            DetailJson = detail is null ? "{}" : JsonSerializer.Serialize(detail),
            CreatedAt = DateTime.UtcNow
        };

    public static OrganizationAuditResponse ToResponse(OrganizationAuditRecord record)
        => new(
            record.Id,
            record.OrganizationId,
            record.ActorKind,
            record.ActorId,
            record.PresenterUserName,
            record.Action,
            record.TargetType,
            record.TargetId,
            record.Outcome,
            record.CreatedAt);
}
