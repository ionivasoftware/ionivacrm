using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="ISocialAccountRepository"/>. Runs from HTTP and from the background publisher.</summary>
public sealed class SocialAccountRepository : ISocialAccountRepository
{
    private readonly ApplicationDbContext _context;

    public SocialAccountRepository(ApplicationDbContext context) => _context = context;

    public Task<List<SocialAccount>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _context.SocialAccounts.IgnoreQueryFilters().Where(a => !a.IsDeleted)
            .OrderBy(a => a.Provider).ThenBy(a => a.Label).ToListAsync(cancellationToken);

    public Task<SocialAccount?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.SocialAccounts.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted, cancellationToken);

    public Task<SocialAccount?> FindByExternalAsync(SocialProvider provider, string externalId, CancellationToken cancellationToken = default) =>
        _context.SocialAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Provider == provider && a.ExternalId == externalId && !a.IsDeleted, cancellationToken);

    public async Task AddAsync(SocialAccount account, CancellationToken cancellationToken = default)
    {
        await _context.SocialAccounts.AddAsync(account, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SocialAccount account, CancellationToken cancellationToken = default)
    {
        account.UpdatedAt = DateTime.UtcNow;
        if (_context.Entry(account).State == EntityState.Detached) _context.SocialAccounts.Update(account);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Hard delete — a soft-deleted token is still a token at rest.
        var deleted = await _context.SocialAccounts.IgnoreQueryFilters().Where(a => a.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }
}
