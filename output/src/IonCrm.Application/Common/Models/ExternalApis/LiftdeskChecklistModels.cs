using System.Text.Json;
using System.Text.Json.Serialization;

namespace IonCrm.Application.Common.Models.ExternalApis;

// Models for the Liftdesk company checklist management API (docs/liftdesk-saas-checklist-contract.md).
// Same api/v1/crm surface + Bearer key as the SaaS integration, but responses are FLAT — no
// {success,data,message} envelope. Field names are camelCase on the wire.

/// <summary>A single checklist line item under a header. <c>Id</c>/<c>SortOrder</c> are response-only.</summary>
public sealed record LiftdeskChecklistItem(
    Guid Id,
    string Text,
    int SortOrder,
    bool IsActive);

/// <summary>A checklist header (group) with its ordered items. <c>Id</c>/<c>SortOrder</c> are response-only.</summary>
public sealed record LiftdeskChecklistHeader(
    Guid Id,
    string Title,
    int SortOrder,
    bool IsActive,
    List<LiftdeskChecklistItem> Items);

/// <summary>
/// A company's full checklist document for one kind ("maintenance" | "fault").
/// <c>Headers</c> come sorted by SortOrder and include inactive (but not deleted) rows.
/// </summary>
public sealed record LiftdeskChecklistDoc(
    int CompanyId,
    string Kind,
    int FormId,
    List<LiftdeskChecklistHeader> Headers,
    /// <summary>
    /// Equipment family of a maintenance list: 1 = elevator (default), 2 = escalator. Always 1 for
    /// the fault list, which is not split by type.
    /// </summary>
    [property: JsonConverter(typeof(LiftdeskChecklistTypeJsonConverter))]
    int Type = LiftdeskChecklistType.Elevator,
    /// <summary>Language of the returned rows (1 = TR, 2 = EN, …).</summary>
    int Culture = LiftdeskChecklistCulture.Turkish,
    /// <summary>
    /// Languages that actually have rows for this form/type — what the CRM fills its language picker
    /// with. Null or single-element means there is nothing to switch between.
    /// </summary>
    List<int>? AvailableCultures = null);

/// <summary>Known checklist languages. Liftdesk stores the raw int, so unknown values pass through.</summary>
public static class LiftdeskChecklistCulture
{
    /// <summary>Turkish — the default for every tenant without an explicit setting.</summary>
    public const int Turkish = 1;

    /// <summary>English.</summary>
    public const int English = 2;
}

/// <summary>
/// Reads the equipment family whether Liftdesk sends it as an enum NAME ("Elevator"/"Escalator" —
/// what its global JsonStringEnumConverter actually produces) or as a number. Tolerating both means
/// a serialization change on either side cannot break the checklist screen again. Unknown values
/// fall back to elevator, which is what every pre-existing row was backfilled to.
/// </summary>
public sealed class LiftdeskChecklistTypeJsonConverter : JsonConverter<int>
{
    /// <inheritdoc />
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Number => reader.TryGetInt32(out var n) ? n : LiftdeskChecklistType.Elevator,
            JsonTokenType.String => ParseName(reader.GetString()),
            _                    => LiftdeskChecklistType.Elevator,
        };

    private static int ParseName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return LiftdeskChecklistType.Elevator;
        if (int.TryParse(value, out var numeric)) return numeric;              // "2"
        return value.Equals("Escalator", StringComparison.OrdinalIgnoreCase)   // "Escalator"
            ? LiftdeskChecklistType.Escalator
            : LiftdeskChecklistType.Elevator;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}

/// <summary>Equipment families a maintenance checklist can belong to (Liftdesk <c>ChecklistType</c>).</summary>
public static class LiftdeskChecklistType
{
    /// <summary>Classic elevator — the default, and what every pre-existing row was backfilled to.</summary>
    public const int Elevator = 1;

    /// <summary>Escalator / moving walkway.</summary>
    public const int Escalator = 2;

    /// <summary>True when <paramref name="type"/> is a known equipment family.</summary>
    public static bool IsValid(int? type) => type is null or Elevator or Escalator;
}

/// <summary>Item input for the full-document PUT. <c>IsActive</c> defaults to true on the Liftdesk side.</summary>
public sealed record LiftdeskChecklistItemInput(
    string Text,
    bool IsActive = true);

/// <summary>
/// Header input for the full-document PUT. Order in the array becomes the SortOrder.
/// <c>IsActive</c> is last so it can default to true (contract: optional, default true) when omitted.
/// </summary>
public sealed record LiftdeskChecklistHeaderInput(
    string Title,
    List<LiftdeskChecklistItemInput> Items,
    bool IsActive = true);

/// <summary>
/// Body of PUT …/{kind}-checklist. This is a FULL-document replace: the sent set becomes the new
/// checklist (an empty list intentionally clears it). No ids / sortOrders are sent.
/// </summary>
public sealed record LiftdeskChecklistUpdateRequest(
    List<LiftdeskChecklistHeaderInput> Headers);

/// <summary>
/// Response of POST …/checklists/reset. <c>Maintenance</c>/<c>Fault</c> are null when that kind
/// was not part of the reset scope.
/// </summary>
public sealed record LiftdeskChecklistResetResponse(
    int CompanyId,
    LiftdeskChecklistDoc? Maintenance,
    LiftdeskChecklistDoc? Fault,
    /// <summary>Escalator maintenance list — populated when kind was "escalator" or "both".</summary>
    LiftdeskChecklistDoc? EscalatorMaintenance = null);
