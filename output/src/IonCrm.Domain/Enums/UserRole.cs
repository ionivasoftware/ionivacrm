namespace IonCrm.Domain.Enums;

/// <summary>
/// Roles available within a project (tenant).
/// SuperAdmin is a flag on the User entity, not a role here.
/// </summary>
public enum UserRole
{
    /// <summary>Can manage project users, settings, and all data.</summary>
    ProjectAdmin = 1,

    /// <summary>Can view and manage the full team pipeline.</summary>
    SalesManager = 2,

    /// <summary>Can only see own customers and tasks.</summary>
    SalesRep = 3,

    /// <summary>Can only view invoices and payments.</summary>
    Accounting = 4
}
