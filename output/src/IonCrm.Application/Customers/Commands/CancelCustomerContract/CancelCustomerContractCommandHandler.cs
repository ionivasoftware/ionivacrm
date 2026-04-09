using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Customers.Commands.CreateCustomerContract;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.CancelCustomerContract;

/// <summary>Handles <see cref="CancelCustomerContractCommand"/>.</summary>
public sealed class CancelCustomerContractCommandHandler
    : IRequestHandler<CancelCustomerContractCommand, Result<CancelCustomerContractDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ICustomerContractRepository _contractRepository;
    private readonly ISaasBClient _saasBClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CancelCustomerContractCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="CancelCustomerContractCommandHandler"/>.</summary>
    public CancelCustomerContractCommandHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ICustomerContractRepository contractRepository,
        ISaasBClient saasBClient,
        ICurrentUserService currentUser,
        ILogger<CancelCustomerContractCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _contractRepository = contractRepository;
        _saasBClient        = saasBClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<CancelCustomerContractDto>> Handle(
        CancelCustomerContractCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Load customer
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<CancelCustomerContractDto>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<CancelCustomerContractDto>.Failure("Bu müşteriye erişim yetkiniz yok.");

        // 2. Verify Rezerval customer (mirror create flow)
        if (string.IsNullOrEmpty(customer.LegacyId)
            || (!customer.LegacyId.StartsWith("SAASB-", StringComparison.OrdinalIgnoreCase)
             && !customer.LegacyId.StartsWith("REZV-", StringComparison.OrdinalIgnoreCase)))
        {
            return Result<CancelCustomerContractDto>.Failure(
                "Bu müşteri Rezerval'dan gelmemiş. Sözleşme yalnızca Rezerval müşterileri için iptal edilebilir.");
        }

        if (!TryExtractRezervalCompanyId(customer.LegacyId, out var rezervalCompanyId))
        {
            return Result<CancelCustomerContractDto>.Failure(
                "Müşterinin Rezerval şirket numarası okunamadı.");
        }

        // 3. Load active contract — nothing to cancel if none exists
        var contract = await _contractRepository.GetActiveByCustomerIdAsync(customer.Id, cancellationToken);
        if (contract is null)
        {
            return Result<CancelCustomerContractDto>.Failure(
                "Müşterinin aktif bir sözleşmesi bulunmuyor.");
        }

        // 4. Load project for Rezerval credentials
        var project = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        if (project is null)
            return Result<CancelCustomerContractDto>.Failure("Proje bulunamadı.");

        var rezervalApiKey = project.RezervAlApiKey;
        if (string.IsNullOrWhiteSpace(rezervalApiKey))
        {
            return Result<CancelCustomerContractDto>.Failure(
                "Bu projede Rezerval API anahtarı yapılandırılmamış.");
        }

        // 5. Call Rezerval cancel endpoint. Tolerant: even if iyzico warnings are returned,
        //    Rezerval still flips its own state and we proceed with local cleanup.
        RezervalCancelSubscriptionResponse rezervalResponse;
        try
        {
            rezervalResponse = await _saasBClient.CancelRezervalSubscriptionAsync(
                new RezervalCancelSubscriptionRequest(rezervalCompanyId),
                rezervalApiKey,
                cancellationToken);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("BrokenCircuit") ||
                                    ex.Message.Contains("circuit is now open", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rezerval circuit breaker open while cancelling for customer {CustomerId}.", customer.Id);
            return Result<CancelCustomerContractDto>.Failure(
                "Rezerval API şu anda geçici olarak erişilemiyor. Lütfen kısa süre sonra tekrar deneyin.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Rezerval subscription cancel failed for customer {CustomerId} (rezervalCompanyId={RezvId}).",
                customer.Id, rezervalCompanyId);
            return Result<CancelCustomerContractDto>.Failure(
                $"Rezerval'da abonelik iptal edilemedi: {ex.Message}");
        }

        var iyzicoWarnings = rezervalResponse.Data?.IyzicoWarnings ?? new List<string>();

        // 6. Local cleanup — wrapped so DB errors return a meaningful 400 instead of 500.
        try
        {
            contract.Status                 = ContractStatus.Cancelled;
            contract.NextInvoiceDate        = null;
            contract.EndDate                = DateTime.UtcNow;
            // Clear dead Rezerval refs — the iyzico pricing plan + product no longer exist.
            contract.RezervalSubscriptionId = null;
            contract.RezervalPaymentPlanId  = null;
            await _contractRepository.UpdateAsync(contract, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Contract cancel persistence failed for customer {CustomerId} (contractId={ContractId}) " +
                "after Rezerval cancel succeeded. Local DB state may be out of sync.",
                customer.Id, contract.Id);

            return Result<CancelCustomerContractDto>.Failure(
                $"Sözleşme iptali yerelde kaydedilemedi: {ex.GetBaseException().Message}");
        }

        _logger.LogInformation(
            "Cancelled contract {ContractId} for customer {CustomerId} ({CompanyName}). " +
            "Iyzico warnings: {WarningCount}.",
            contract.Id, customer.Id, customer.CompanyName, iyzicoWarnings.Count);

        return Result<CancelCustomerContractDto>.Success(
            new CancelCustomerContractDto(
                Contract:        CreateCustomerContractCommandHandler.MapToDto(contract),
                IyzicoWarnings:  iyzicoWarnings));
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
}
