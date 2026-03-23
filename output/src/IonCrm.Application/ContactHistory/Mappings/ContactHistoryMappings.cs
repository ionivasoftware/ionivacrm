using IonCrm.Application.Common.DTOs;

namespace IonCrm.Application.ContactHistory.Mappings;

/// <summary>Extension methods for mapping ContactHistory entities to DTOs.</summary>
public static class ContactHistoryMappings
{
    /// <summary>Maps a ContactHistory entity to a <see cref="ContactHistoryDto"/>.</summary>
    public static ContactHistoryDto ToDto(this Domain.Entities.ContactHistory h) => new()
    {
        Id = h.Id,
        CustomerId = h.CustomerId,
        ProjectId = h.ProjectId,
        Type = h.Type,
        Subject = h.Subject,
        Content = h.Content,
        Outcome = h.Outcome,
        ContactedAt = h.ContactedAt,
        CreatedByUserId = h.CreatedByUserId,
        CreatedByUserName = h.CreatedByUser is not null
            ? $"{h.CreatedByUser.FirstName} {h.CreatedByUser.LastName}".Trim()
            : null,
        CreatedAt = h.CreatedAt,
        UpdatedAt = h.UpdatedAt
    };
}
