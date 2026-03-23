using IonCrm.Application.Common.DTOs;
using IonCrm.Domain.Entities;

namespace IonCrm.Application.Tasks.Mappings;

/// <summary>Extension methods for mapping CustomerTask entities to DTOs.</summary>
public static class CustomerTaskMappings
{
    /// <summary>Maps a <see cref="CustomerTask"/> entity to a <see cref="CustomerTaskDto"/>.</summary>
    public static CustomerTaskDto ToDto(this CustomerTask t) => new()
    {
        Id = t.Id,
        CustomerId = t.CustomerId,
        ProjectId = t.ProjectId,
        Title = t.Title,
        Description = t.Description,
        DueDate = t.DueDate,
        Priority = t.Priority,
        Status = t.Status,
        AssignedUserId = t.AssignedUserId,
        AssignedUserName = t.AssignedUser is not null
            ? $"{t.AssignedUser.FirstName} {t.AssignedUser.LastName}".Trim()
            : null,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };
}
