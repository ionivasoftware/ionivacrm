using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Interfaces;

/// <summary>Connected social accounts (tokens encrypted). Disconnect is a hard delete — a soft-deleted token is still a token.</summary>
public interface ISocialAccountRepository
{
    Task<List<SocialAccount>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SocialAccount?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SocialAccount?> FindByExternalAsync(SocialProvider provider, string externalId, CancellationToken cancellationToken = default);

    Task AddAsync(SocialAccount account, CancellationToken cancellationToken = default);

    Task UpdateAsync(SocialAccount account, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
