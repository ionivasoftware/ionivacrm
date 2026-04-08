using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.CreateCustomerContract;

/// <summary>Handles <see cref="CreateCustomerContractCommand"/>.</summary>
public sealed class CreateCustomerContractCommandHandler
    : IRequestHandler<CreateCustomerContractCommand, Result<CustomerContractDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ICustomerContractRepository _contractRepository;
    private readonly ISaasBClient _saasBClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CreateCustomerContractCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="CreateCustomerContractCommandHandler"/>.</summary>
    public CreateCustomerContractCommandHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ICustomerContractRepository contractRepository,
        ISaasBClient saasBClient,
        ICurrentUserService currentUser,
        ILogger<CreateCustomerContractCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _contractRepository = contractRepository;
        _saasBClient        = saasBClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerContractDto>> Handle(
        CreateCustomerContractCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Load customer
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<CustomerContractDto>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<CustomerContractDto>.Failure("Bu müşteriye erişim yetkiniz yok.");

        // 2. Verify this is a Rezerval customer.
        //    Accepted LegacyId formats:
        //      "SAASB-{id}" → synced from Rezerval (legacy prefix)
        //      "REZV-{id}"  → created in CRM and pushed to Rezerval
        if (string.IsNullOrEmpty(customer.LegacyId)
            || (!customer.LegacyId.StartsWith("SAASB-", StringComparison.OrdinalIgnoreCase)
             && !customer.LegacyId.StartsWith("REZV-", StringComparison.OrdinalIgnoreCase)))
        {
            return Result<CustomerContractDto>.Failure(
                "Bu müşteri Rezerval'dan gelmemiş. Sözleşme yalnızca Rezerval müşterileri için oluşturulabilir.");
        }

        if (!TryExtractRezervalCompanyId(customer.LegacyId, out var rezervalCompanyId))
        {
            return Result<CustomerContractDto>.Failure(
                "Müşterinin Rezerval şirket numarası okunamadı.");
        }

        // 3. Get project + Rezerval credentials
        var project = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        if (project is null)
            return Result<CustomerContractDto>.Failure("Proje bulunamadı.");

        var rezervalApiKey = project.RezervAlApiKey;
        if (string.IsNullOrWhiteSpace(rezervalApiKey))
        {
            return Result<CustomerContractDto>.Failure(
                "Bu projede Rezerval API anahtarı yapılandırılmamış.");
        }

        // 4. Normalize start date to UTC midnight
        var startDate = DateTime.SpecifyKind(request.StartDate.Date, DateTimeKind.Utc);
        var endDate = request.DurationMonths.HasValue
            ? (DateTime?)startDate.AddMonths(request.DurationMonths.Value)
            : null;

        // 5. Call Rezerval to create iyzico subscription + payment plan
        RezervalSubscriptionResponse rezervalResponse;
        try
        {
            rezervalResponse = await _saasBClient.CreateRezervalSubscriptionAsync(
                new RezervalSubscriptionRequest(
                    RezervalCompanyId: rezervalCompanyId,
                    SubscriptionName: $"{customer.CompanyName} Abonelik",
                    MonthlyAmount: request.MonthlyAmount,
                    PaymentType: request.PaymentType.ToString(),
                    StartDate: startDate.ToString("yyyy-MM-dd"),
                    DurationMonths: request.DurationMonths,
                    Currency: "TRY"),
                rezervalApiKey,
                cancellationToken);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("BrokenCircuit") ||
                                    ex.Message.Contains("circuit is now open", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rezerval circuit breaker open for customer {CustomerId}.", customer.Id);
            return Result<CustomerContractDto>.Failure(
                "Rezerval API şu anda geçici olarak erişilemiyor. Lütfen kısa süre sonra tekrar deneyin.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Rezerval subscription creation failed for customer {CustomerId} (rezervalCompanyId={RezvId}).",
                customer.Id, rezervalCompanyId);
            return Result<CustomerContractDto>.Failure(
                $"Rezerval'da abonelik oluşturulamadı: {ex.Message}");
        }

        // 6-8. DB writes — wrapped so any persistence error surfaces with a meaningful message
        // instead of bubbling up as a generic 500. Rezerval has already been hit at this point
        // so a failure here means the local state is out of sync; logging is critical.
        CustomerContract saved;
        try
        {
            // 6. Renewal semantics: complete any currently-active contract before adding the new one.
            var existingActive = await _contractRepository.GetActiveByCustomerIdAsync(customer.Id, cancellationToken);
            if (existingActive is not null)
            {
                existingActive.Status = ContractStatus.Completed;
                existingActive.NextInvoiceDate = null;
                await _contractRepository.UpdateAsync(existingActive, cancellationToken);

                _logger.LogInformation(
                    "Marked previous contract {OldId} Completed before renewal for customer {CustomerId}.",
                    existingActive.Id, customer.Id);
            }

            // 7. Insert new contract
            var contract = new CustomerContract
            {
                ProjectId               = customer.ProjectId,
                CustomerId              = customer.Id,
                Title                   = $"{customer.CompanyName} Abonelik",
                MonthlyAmount           = request.MonthlyAmount,
                PaymentType             = request.PaymentType,
                StartDate               = startDate,
                DurationMonths          = request.DurationMonths,
                EndDate                 = endDate,
                Status                  = ContractStatus.Active,
                RezervalSubscriptionId  = rezervalResponse.Data?.RezervalSubscriptionId,
                RezervalPaymentPlanId   = rezervalResponse.Data?.RezervalPaymentPlanId,
                // EFT: schedule first invoice on the start date. CreditCard: never auto-invoice (iyzico handles it).
                NextInvoiceDate         = request.PaymentType == ContractPaymentType.EftWire ? startDate : (DateTime?)null,
                LastInvoiceGeneratedDate = null
            };

            saved = await _contractRepository.AddAsync(contract, cancellationToken);

            // 8. Sync MonthlyLicenseFee on the Customer record so the existing
            //    CreateRezervAlDraftInvoice flow (manual) stays in agreement with the contract price.
            if (customer.MonthlyLicenseFee != request.MonthlyAmount)
            {
                customer.MonthlyLicenseFee = request.MonthlyAmount;
                await _customerRepository.UpdateAsync(customer, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Contract persistence failed for customer {CustomerId} after Rezerval succeeded " +
                "(rezervalSubId={SubId}). Local DB state may be out of sync.",
                customer.Id, rezervalResponse.Data?.RezervalSubscriptionId);

            return Result<CustomerContractDto>.Failure(
                $"Sözleşme kaydedilemedi: {ex.GetBaseException().Message}");
        }

        _logger.LogInformation(
            "Created contract {ContractId} for customer {CustomerId} ({CompanyName}). " +
            "Amount={Amount} Type={Type} Start={Start:d} Duration={Duration}.",
            saved.Id, customer.Id, customer.CompanyName,
            request.MonthlyAmount, request.PaymentType, startDate, request.DurationMonths);

        return Result<CustomerContractDto>.Success(MapToDto(saved));
    }

    private static bool TryExtractRezervalCompanyId(string legacyId, out int rezervalCompanyId)
    {
        rezervalCompanyId = 0;

        string raw = legacyId switch
        {
            _ when legacyId.StartsWith("SAASB-", StringComparison.OrdinalIgnoreCase) => legacyId["SAASB-".Length..],
            _ when legacyId.StartsWith("REZV-",  StringComparison.OrdinalIgnoreCase) => legacyId["REZV-".Length..],
            _ => legacyId
        };

        return int.TryParse(raw, out rezervalCompanyId);
    }

    internal static CustomerContractDto MapToDto(CustomerContract c) =>
        new(
            Id:                       c.Id,
            CustomerId:               c.CustomerId,
            Title:                    c.Title,
            MonthlyAmount:            c.MonthlyAmount,
            PaymentType:              c.PaymentType,
            StartDate:                c.StartDate,
            DurationMonths:           c.DurationMonths,
            EndDate:                  c.EndDate,
            Status:                   c.Status,
            RezervalSubscriptionId:   c.RezervalSubscriptionId,
            RezervalPaymentPlanId:    c.RezervalPaymentPlanId,
            NextInvoiceDate:          c.NextInvoiceDate,
            LastInvoiceGeneratedDate: c.LastInvoiceGeneratedDate,
            CreatedAt:                c.CreatedAt);
}
