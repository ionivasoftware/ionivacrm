using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Commands.AddCustomerSms;

/// <summary>Adds SMS credits to an EMS customer and creates a local CRM draft invoice.</summary>
public record AddCustomerSmsCommand(
    Guid CustomerId,
    int Count)
    : IRequest<Result<AddCustomerSmsDto>>;

/// <summary>Result after a successful SMS credit addition.</summary>
public record AddCustomerSmsDto(
    int CompanyId,
    int SmsCount,
    int Added,
    bool InvoiceCreated,
    Guid? InvoiceId);
