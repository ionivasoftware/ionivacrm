using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Interfaces;

public interface ISocialProfileRepository
{
    Task<List<SocialProfile>> ListAsync(string language, CancellationToken cancellationToken = default);

    Task<SocialProfile?> GetAsync(SocialProvider provider, string language, CancellationToken cancellationToken = default);

    Task AddAsync(SocialProfile profile, CancellationToken cancellationToken = default);

    Task UpdateAsync(SocialProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Hard delete — "back to defaults".</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
