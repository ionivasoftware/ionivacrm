using IonCrm.Application.Common.DTOs;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>Read-only counts for the Cavit desktop assistant.</summary>
public interface ICavitStatsRepository
{
    /// <summary>Stats for the project whose name contains <paramref name="projectName"/>; null if none matches.</summary>
    Task<CavitStatsDto?> GetAsync(string projectName, DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>Names of all live projects (for a helpful "not found" answer).</summary>
    Task<List<string>> ProjectNamesAsync(CancellationToken cancellationToken = default);
}
