using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Commands.CreateRezervAlDraftInvoice;

/// <summary>
/// Creates a draft invoice for a RezervAl customer using the customer's
/// <c>MonthlyLicenseFee</c> as the unit price for "RezervAl Aylık Lisans Bedeli".
/// Unlike EMS products (where price is stored in ParasutProducts configuration),
/// RezervAl customers have an individual monthly fee stored on the Customer record.
/// </summary>
public record CreateRezervAlDraftInvoiceCommand : IRequest<Result<InvoiceDto>>
{
    /// <summary>The CRM customer ID. Must be a RezervAl customer (LegacyId starts with "REZV-").</summary>
    public Guid CustomerId { get; init; }
}
