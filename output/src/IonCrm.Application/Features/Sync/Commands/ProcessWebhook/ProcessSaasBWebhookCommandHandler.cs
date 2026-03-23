using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IonCrm.Application.Features.Sync.Commands.ProcessWebhook;

/// <summary>
/// Handles inbound SaaS B webhook events.
/// SaaS B uses different field names than SaaS A (CustomerId, FullName, AccountState, etc.).
/// Upserts CRM entities and records the sync to SyncLogs.
/// </summary>
public sealed class ProcessSaasBWebhookCommandHandler
    : IRequestHandler<ProcessSaasBWebhookCommand, Result>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ISyncLogRepository _syncLogRepository;
    private readonly ILogger<ProcessSaasBWebhookCommandHandler> _logger;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>Initialises a new instance of <see cref="ProcessSaasBWebhookCommandHandler"/>.</summary>
    public ProcessSaasBWebhookCommandHandler(
        ICustomerRepository customerRepository,
        ISyncLogRepository syncLogRepository,
        ILogger<ProcessSaasBWebhookCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _syncLogRepository = syncLogRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        ProcessSaasBWebhookCommand request,
        CancellationToken cancellationToken)
    {
        var log = new SyncLog
        {
            Id = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            Source = SyncSource.SaasB,
            Direction = SyncDirection.Inbound,
            EntityType = request.Type,
            EntityId = request.Id,
            Status = SyncStatus.Pending,
            Payload = request.RawPayload
        };

        await _syncLogRepository.AddAsync(log, cancellationToken);

        try
        {
            if (request.Type.Equals("customer", StringComparison.OrdinalIgnoreCase))
            {
                var customer = JsonSerializer.Deserialize<SaasBCustomer>(
                    request.RawPayload, JsonOpts);

                if (customer is null)
                    return Result.Failure("Failed to deserialize SaaS B customer payload.");

                await UpsertCustomerFromSaasBAsync(customer, request.ProjectId, cancellationToken);
            }

            log.Status = SyncStatus.Success;
            log.SyncedAt = DateTime.UtcNow;
            await _syncLogRepository.UpdateAsync(log, cancellationToken);

            _logger.LogInformation(
                "SaaS B webhook processed. Event={Event} Entity={Type}/{Id}",
                request.Event, request.Type, request.Id);

            return Result.Success();
        }
        catch (Exception ex)
        {
            log.Status = SyncStatus.Failed;
            log.ErrorMessage = ex.Message;
            await _syncLogRepository.UpdateAsync(log, cancellationToken);

            _logger.LogError(ex,
                "SaaS B webhook processing failed. Event={Event} Entity={Type}/{Id}",
                request.Event, request.Type, request.Id);

            return Result.Failure($"SaaS B webhook processing failed: {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task UpsertCustomerFromSaasBAsync(
        SaasBCustomer saasCustomer,
        Guid projectId,
        CancellationToken ct)
    {
        var legacyId = $"SAASB-{saasCustomer.CustomerId}";
        var existing = await _customerRepository.GetByLegacyIdAsync(legacyId, ct);

        if (existing is not null)
        {
            existing.CompanyName = saasCustomer.FullName;
            existing.Email = saasCustomer.ContactEmail;
            existing.Phone = saasCustomer.Mobile;
            existing.Address = saasCustomer.StreetAddress;
            existing.TaxNumber = saasCustomer.TaxId;
            existing.Status = MapSaasBStatus(saasCustomer.AccountState);
            existing.Segment = MapSaasBTier(saasCustomer.Tier);
            await _customerRepository.UpdateAsync(existing, ct);
        }
        else
        {
            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                LegacyId = legacyId,
                CompanyName = saasCustomer.FullName,
                Email = saasCustomer.ContactEmail,
                Phone = saasCustomer.Mobile,
                Address = saasCustomer.StreetAddress,
                TaxNumber = saasCustomer.TaxId,
                Status = MapSaasBStatus(saasCustomer.AccountState),
                Segment = MapSaasBTier(saasCustomer.Tier)
            };
            await _customerRepository.AddAsync(customer, ct);
        }
    }

    private static CustomerStatus MapSaasBStatus(string state) => state.ToUpper() switch
    {
        "ACTIVE" => CustomerStatus.Active,
        "LEAD" => CustomerStatus.Lead,
        "PASSIVE" or "INACTIVE" => CustomerStatus.Inactive,
        "CHURNED" => CustomerStatus.Churned,
        _ => CustomerStatus.Lead
    };

    private static CustomerSegment? MapSaasBTier(string? tier) =>
        tier?.ToUpper() switch
        {
            "ENTERPRISE" => CustomerSegment.Enterprise,
            "SME" => CustomerSegment.SME,
            "INDIVIDUAL" => CustomerSegment.Individual,
            _ => null
        };
}
