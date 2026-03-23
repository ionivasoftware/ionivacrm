namespace IonCrm.Domain.Enums;

/// <summary>Direction of a sync operation relative to ION CRM.</summary>
public enum SyncDirection
{
    /// <summary>Data coming from external system into ION CRM.</summary>
    Inbound = 1,

    /// <summary>Data going from ION CRM to an external system.</summary>
    Outbound = 2
}
