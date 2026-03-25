using IonCrm.Application.Common.DTOs;

namespace IonCrm.Application.Opportunities.Mappings;

/// <summary>Extension methods for mapping Opportunity entities to DTOs.</summary>
public static class OpportunityMappings
{
    public static OpportunityDto ToDto(this Domain.Entities.Opportunity o) => new()
    {
        Id = o.Id,
        CustomerId = o.CustomerId,
        CustomerName = o.Customer?.CompanyName ?? string.Empty,
        ProjectId = o.ProjectId,
        Title = o.Title,
        Value = o.Value,
        Stage = o.Stage,
        Probability = o.Probability,
        ExpectedCloseDate = o.ExpectedCloseDate,
        AssignedUserId = o.AssignedUserId,
        AssignedUserName = o.AssignedUser is not null
            ? $"{o.AssignedUser.FirstName} {o.AssignedUser.LastName}".Trim()
            : null,
        CreatedAt = o.CreatedAt,
        UpdatedAt = o.UpdatedAt
    };
}
