using IonCrm.Application.Common.DTOs;
using IonCrm.Domain.Entities;

namespace IonCrm.Application.Customers.Mappings;

/// <summary>Extension methods for mapping <see cref="Customer"/> to DTOs.</summary>
public static class CustomerMappings
{
    /// <summary>Maps a <see cref="Customer"/> entity to a <see cref="CustomerDto"/>.</summary>
    public static CustomerDto ToDto(this Customer c) => new()
    {
        Id = c.Id,
        ProjectId = c.ProjectId,
        Code = c.Code,
        CompanyName = c.CompanyName,
        ContactName = c.ContactName,
        Email = c.Email,
        Phone = c.Phone,
        Address = c.Address,
        TaxNumber = c.TaxNumber,
        TaxUnit = c.TaxUnit,
        Status = c.Status,
        Segment = c.Segment,
        Label = c.Label,
        AssignedUserId = c.AssignedUserId,
        AssignedUserName = c.AssignedUser is not null
            ? $"{c.AssignedUser.FirstName} {c.AssignedUser.LastName}".Trim()
            : null,
        ExpirationDate = c.ExpirationDate,
        LogoUrl = c.LogoUrl,
        LegacyId = c.LegacyId,
        ParasutContactId = c.ParasutContactId,
        IsEInvoicePayer = c.IsEInvoicePayer,
        EInvoiceAddress = c.EInvoiceAddress,
        MonthlyLicenseFee = c.MonthlyLicenseFee,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };
}
