using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Sync.Commands.ProcessWebhook;

/// <summary>
/// Processes an inbound webhook payload pushed by SaaS A to POST /api/v1/sync/saas-a.
/// Parses the event, upserts affected CRM entities, and logs the sync.
/// </summary>
public record ProcessSaasAWebhookCommand(
    /// <summary>The event type sent by SaaS A (e.g. "customer.updated").</summary>
    string EventType,

    /// <summary>External entity type: "customer" | "subscription" | "order".</summary>
    string EntityType,

    /// <summary>External entity identifier in SaaS A.</summary>
    string EntityId,

    /// <summary>The tenant project this entity belongs to.</summary>
    Guid ProjectId,

    /// <summary>Raw JSON payload from the SaaS A webhook body.</summary>
    string RawPayload
) : IRequest<Result>;
