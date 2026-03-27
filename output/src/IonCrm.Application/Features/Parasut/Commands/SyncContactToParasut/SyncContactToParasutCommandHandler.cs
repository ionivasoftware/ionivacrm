using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut.Commands.SyncContactToParasut;

/// <summary>Handles <see cref="SyncContactToParasutCommand"/>.</summary>
public sealed class SyncContactToParasutCommandHandler
    : IRequestHandler<SyncContactToParasutCommand, Result<SyncContactToParasutDto>>
{
    private readonly IParasutClient _parasutClient;
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ILogger<SyncContactToParasutCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="SyncContactToParasutCommandHandler"/>.</summary>
    public SyncContactToParasutCommandHandler(
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        ICustomerRepository customerRepository,
        ILogger<SyncContactToParasutCommandHandler> logger)
    {
        _parasutClient = parasutClient;
        _connectionRepository = connectionRepository;
        _customerRepository = customerRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<SyncContactToParasutDto>> Handle(
        SyncContactToParasutCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Load connection
        var connection = await _connectionRepository.GetByProjectIdAsync(
            request.ProjectId, cancellationToken);

        if (connection is null || !connection.IsConnected)
            return Result<SyncContactToParasutDto>.Failure(
                "Paraşüt bağlantısı bulunamadı veya token süresi dolmuş. Lütfen tekrar bağlanın.");

        // 2. Load customer
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<SyncContactToParasutDto>.Failure("Müşteri bulunamadı.");

        try
        {
            // 3. Map CRM customer → Paraşüt contact attributes
            var attributes = new ParasutContactAttributes(
                Name:        customer.CompanyName,
                Email:       customer.Email,
                Phone:       customer.Phone,
                ContactType: "company",
                AccountType: "customer",
                TaxNumber:   customer.TaxNumber,
                TaxOffice:   customer.TaxUnit,
                Address:     customer.Address,
                City:        null,
                District:    null
            );

            // 4. Create or update in Paraşüt
            // We use LegacyId format "PARASUT-{contactId}" stored in Segment or a dedicated field
            // to track which Paraşüt contact id corresponds to this CRM customer.
            // For now: always create — a full upsert requires storing the Paraşüt contact ID on the customer.
            var result = await _parasutClient.CreateContactAsync(
                connection.AccessToken!,
                connection.CompanyId,
                attributes,
                cancellationToken);

            var parasutId   = result.Data.Id ?? string.Empty;
            var parasutName = result.Data.Attributes.Name;

            _logger.LogInformation(
                "Synced customer {CustomerId} to Paraşüt contact {ParasutId}.",
                request.CustomerId, parasutId);

            return Result<SyncContactToParasutDto>.Success(new SyncContactToParasutDto(
                CustomerId:        request.CustomerId,
                ParasutContactId:  parasutId,
                ParasutContactName: parasutName,
                WasCreated:        true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to sync customer {CustomerId} to Paraşüt.", request.CustomerId);
            return Result<SyncContactToParasutDto>.Failure(
                $"Paraşüt senkronizasyonu başarısız: {ex.Message}");
        }
    }
}
