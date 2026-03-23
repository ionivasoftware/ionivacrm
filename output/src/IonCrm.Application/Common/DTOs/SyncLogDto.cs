using IonCrm.Domain.Enums;

namespace IonCrm.Application.Common.DTOs;

/// <summary>Read-only projection of a <see cref="IonCrm.Domain.Entities.SyncLog"/> for API responses.</summary>
public record SyncLogDto(
    Guid Id,
    Guid ProjectId,
    string Source,
    string Direction,
    string EntityType,
    string? EntityId,
    string Status,
    string? ErrorMessage,
    int RetryCount,
    DateTime? SyncedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
