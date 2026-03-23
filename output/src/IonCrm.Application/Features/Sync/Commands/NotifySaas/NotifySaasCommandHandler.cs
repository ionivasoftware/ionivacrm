using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Sync.Commands.NotifySaas;

/// <summary>
/// Handles <see cref="NotifySaasCommand"/> — fires instant callbacks to SaaS A and/or SaaS B.
/// Each callback attempt is logged to SyncLogs with Success or Failed status.
/// </summary>
public sealed class NotifySaasCommandHandler : IRequestHandler<NotifySaasCommand, Result>
{
    private readonly ISaasAClient _saasAClient;
    private readonly ISaasBClient _saasBClient;
    private readonly ISyncLogRepository _syncLogRepository;
    private readonly ILogger<NotifySaasCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="NotifySaasCommandHandler"/>.</summary>
    public NotifySaasCommandHandler(
        ISaasAClient saasAClient,
        ISaasBClient saasBClient,
        ISyncLogRepository syncLogRepository,
        ILogger<NotifySaasCommandHandler> logger)
    {
        _saasAClient = saasAClient;
        _saasBClient = saasBClient;
        _syncLogRepository = syncLogRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(NotifySaasCommand request, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        if (request.NotifySaasA)
        {
            var result = await NotifySaasAAsync(request, cancellationToken);
            if (!result.IsSuccess)
                errors.AddRange(result.Errors);
        }

        if (request.NotifySaasB)
        {
            var result = await NotifySaasBAsync(request, cancellationToken);
            if (!result.IsSuccess)
                errors.AddRange(result.Errors);
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<Result> NotifySaasAAsync(NotifySaasCommand request, CancellationToken ct)
    {
        var log = new SyncLog
        {
            Id = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            Source = SyncSource.SaasA,
            Direction = SyncDirection.Outbound,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            Status = SyncStatus.Pending,
            Payload = request.PayloadJson
        };

        await _syncLogRepository.AddAsync(log, ct);

        try
        {
            var payload = new SaasACallbackPayload(
                EventType: request.EventType,
                EntityType: request.EntityType,
                EntityId: request.EntityId,
                ProjectId: request.ProjectId.ToString(),
                Data: request.PayloadJson,
                OccurredAt: DateTime.UtcNow);

            await _saasAClient.NotifyCallbackAsync(payload, ct);

            log.Status = SyncStatus.Success;
            log.SyncedAt = DateTime.UtcNow;
            await _syncLogRepository.UpdateAsync(log, ct);

            _logger.LogInformation(
                "SaaS A callback succeeded. Event={EventType} Entity={EntityType}/{EntityId}",
                request.EventType, request.EntityType, request.EntityId);

            return Result.Success();
        }
        catch (Exception ex)
        {
            log.Status = SyncStatus.Failed;
            log.ErrorMessage = ex.Message;
            await _syncLogRepository.UpdateAsync(log, ct);

            _logger.LogWarning(ex,
                "SaaS A callback failed. Event={EventType} Entity={EntityType}/{EntityId}",
                request.EventType, request.EntityType, request.EntityId);

            return Result.Failure($"SaaS A callback failed: {ex.Message}");
        }
    }

    private async Task<Result> NotifySaasBAsync(NotifySaasCommand request, CancellationToken ct)
    {
        var log = new SyncLog
        {
            Id = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            Source = SyncSource.SaasB,
            Direction = SyncDirection.Outbound,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            Status = SyncStatus.Pending,
            Payload = request.PayloadJson
        };

        await _syncLogRepository.AddAsync(log, ct);

        try
        {
            var payload = new SaasBCallbackPayload(
                Event: $"crm.{request.EventType}",
                Id: request.EntityId,
                Type: request.EntityType,
                Project: request.ProjectId.ToString(),
                Payload: request.PayloadJson,
                Timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            await _saasBClient.NotifyCallbackAsync(payload, ct);

            log.Status = SyncStatus.Success;
            log.SyncedAt = DateTime.UtcNow;
            await _syncLogRepository.UpdateAsync(log, ct);

            _logger.LogInformation(
                "SaaS B callback succeeded. Event={EventType} Entity={EntityType}/{EntityId}",
                request.EventType, request.EntityType, request.EntityId);

            return Result.Success();
        }
        catch (Exception ex)
        {
            log.Status = SyncStatus.Failed;
            log.ErrorMessage = ex.Message;
            await _syncLogRepository.UpdateAsync(log, ct);

            _logger.LogWarning(ex,
                "SaaS B callback failed. Event={EventType} Entity={EntityType}/{EntityId}",
                request.EventType, request.EntityType, request.EntityId);

            return Result.Failure($"SaaS B callback failed: {ex.Message}");
        }
    }
}
