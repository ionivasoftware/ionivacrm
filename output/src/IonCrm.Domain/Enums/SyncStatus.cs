namespace IonCrm.Domain.Enums;

/// <summary>Current processing status of a sync log entry.</summary>
public enum SyncStatus
{
    Pending = 1,
    Success = 2,
    Failed = 3,
    Retrying = 4
}
