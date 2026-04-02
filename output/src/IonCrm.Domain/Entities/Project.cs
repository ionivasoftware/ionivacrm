using IonCrm.Domain.Common;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Represents a tenant (project) in the ION CRM multi-tenant system.
/// Examples: "Ioniva Muhasebe", "Ioniva Satis".
/// </summary>
public class Project : BaseEntity
{
    /// <summary>Gets or sets the display name of the project.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets an optional description of the project.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets a value indicating whether this project is active.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Gets or sets the base URL of the EMS (SaaS A) API for this project.</summary>
    public string? EmsBaseUrl { get; set; }

    /// <summary>Gets or sets the API key used to authenticate with EMS (SaaS A).</summary>
    public string? EmsApiKey { get; set; }

    /// <summary>Gets or sets the base URL of the RezervAl (SaaS B) API for this project.</summary>
    public string? RezervAlBaseUrl { get; set; }

    /// <summary>Gets or sets the API key used to authenticate with Rezerval (SaaS B).</summary>
    public string? RezervAlApiKey { get; set; }

    /// <summary>Gets or sets the SMS credit balance for this project/company.</summary>
    public int SmsCount { get; set; } = 0;

    // Navigation properties
    /// <summary>Gets or sets the user-project role assignments for this project.</summary>
    public ICollection<UserProjectRole> UserProjectRoles { get; set; } = new List<UserProjectRole>();

    /// <summary>Gets or sets the customers belonging to this project.</summary>
    public ICollection<Customer> Customers { get; set; } = new List<Customer>();

    /// <summary>Gets or sets the sync logs for this project.</summary>
    public ICollection<SyncLog> SyncLogs { get; set; } = new List<SyncLog>();
}
