using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Sync.Commands.NotifySaas;

/// <summary>
/// Sends an instant outbound notification to SaaS A and/or SaaS B
/// when a CRM event occurs (subscription extended, status changed, etc.).
/// Logs the outbound sync to the SyncLogs table.
/// </summary>
public record NotifySaasCommand(
    /// <summary>Event type identifier, e.g. "subscription_extended", "status_changed".</summary>
    string EventType,

    /// <summary>CRM entity type: "customer", "subscription".</summary>
    string EntityType,

    /// <summary>CRM entity primary key (string representation).</summary>
    string EntityId,

    /// <summary>Tenant project that owns this entity.</summary>
    Guid ProjectId,

    /// <summary>Serialised JSON payload describing what changed.</summary>
    string PayloadJson,

    /// <summary>Whether to notify SaaS A. Defaults to true.</summary>
    bool NotifySaasA = true,

    /// <summary>Whether to notify SaaS B. Defaults to true.</summary>
    bool NotifySaasB = true
) : IRequest<Result>;
