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
/// Handles inbound SaaS A webhook events.
/// Upserts CRM entities and records the sync to SyncLogs.
/// </summary>
public sealed class ProcessSaasAWebhookCommandHandler
    : IRequestHandler<ProcessSaasAWebhookCommand, Result>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ISyncLogRepository _syncLogRepository;
    private readonly ILogger<ProcessSaasAWebhookCommandHandler> _logger;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>Initialises a new instance of <see cref="ProcessSaasAWebhookCommandHandler"/>.</summary>
    public ProcessSaasAWebhookCommandHandler(
        ICustomerRepository customerRepository,
        ISyncLogRepository syncLogRepository,
        ILogger<ProcessSaasAWebhookCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _syncLogRepository = syncLogRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        ProcessSaasAWebhookCommand request,
        CancellationToken cancellationToken)
    {
        var log = new SyncLog
        {
            Id = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            Source = SyncSource.SaasA,
            Direction = SyncDirection.Inbound,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            Status = SyncStatus.Pending,
            Payload = request.RawPayload
        };

        await _syncLogRepository.AddAsync(log, cancellationToken);

        try
        {
            if (request.EntityType.Equals("customer", StringComparison.OrdinalIgnoreCase))
            {
                var customer = JsonSerializer.Deserialize<SaasACustomer>(
                    request.RawPayload, JsonOpts);

                if (customer is null)
                    return Result.Failure("Failed to deserialize SaaS A customer payload.");

                await UpsertCustomerFromSaasAAsync(customer, request.ProjectId, cancellationToken);
            }

            log.Status = SyncStatus.Success;
            log.SyncedAt = DateTime.UtcNow;
            await _syncLogRepository.UpdateAsync(log, cancellationToken);

            _logger.LogInformation(
                "SaaS A webhook processed. Event={EventType} Entity={EntityType}/{EntityId}",
                request.EventType, request.EntityType, request.EntityId);

            return Result.Success();
        }
        catch (Exception ex)
        {
            log.Status = SyncStatus.Failed;
            log.ErrorMessage = ex.Message;
            await _syncLogRepository.UpdateAsync(log, cancellationToken);

            _logger.LogError(ex,
                "SaaS A webhook processing failed. Event={EventType} Entity={EntityType}/{EntityId}",
                request.EventType, request.EntityType, request.EntityId);

            return Result.Failure($"SaaS A webhook processing failed: {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task UpsertCustomerFromSaasAAsync(
        SaasACustomer saasCustomer,
        Guid projectId,
        CancellationToken ct)
    {
        var legacyId = $"SAASA-{saasCustomer.Id}";
        var existing = await _customerRepository.GetByLegacyIdAsync(legacyId, ct);

        if (existing is not null)
        {
            // Update existing customer
            existing.CompanyName = saasCustomer.Name;
            existing.Email = saasCustomer.Email;
            existing.Phone = saasCustomer.Phone;
            existing.Address = saasCustomer.Address;
            existing.TaxNumber = saasCustomer.TaxNumber;
            existing.Status = MapSaasAStatus(saasCustomer.Status);
            existing.Segment = saasCustomer.Segment; // free string, project-specific
            await _customerRepository.UpdateAsync(existing, ct);
        }
        else
        {
            // Insert new customer
            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                LegacyId = legacyId,
                CompanyName = saasCustomer.Name,
                Email = saasCustomer.Email,
                Phone = saasCustomer.Phone,
                Address = saasCustomer.Address,
                TaxNumber = saasCustomer.TaxNumber,
                Status = MapSaasAStatus(saasCustomer.Status),
                Segment = saasCustomer.Segment // free string, project-specific
            };
            await _customerRepository.AddAsync(customer, ct);
        }
    }

    private static CustomerStatus MapSaasAStatus(string status) => status.ToLower() switch
    {
        "active" => CustomerStatus.Active,
        "lead" => CustomerStatus.Lead,
        "demo" or "trial" => CustomerStatus.Demo,
        "passive" or "inactive" => CustomerStatus.Demo, // legacy mapping → Demo
        "churned" => CustomerStatus.Churned,
        _ => CustomerStatus.Lead
    };
}
