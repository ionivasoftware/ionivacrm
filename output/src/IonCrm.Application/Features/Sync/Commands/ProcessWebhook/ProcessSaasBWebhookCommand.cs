using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Sync.Commands.ProcessWebhook;

/// <summary>
/// Processes an inbound webhook payload pushed by SaaS B to POST /api/v1/sync/saas-b.
/// Parses the event, upserts affected CRM entities, and logs the sync.
/// </summary>
public record ProcessSaasBWebhookCommand(
    /// <summary>The event type sent by SaaS B (e.g. "customer.updated").</summary>
    string Event,

    /// <summary>External entity type: "customer" | "subscription" | "order".</summary>
    string Type,

    /// <summary>External entity identifier in SaaS B.</summary>
    string Id,

    /// <summary>The tenant project this entity belongs to.</summary>
    Guid ProjectId,

    /// <summary>Raw JSON payload from the SaaS B webhook body.</summary>
    string RawPayload
) : IRequest<Result>;
