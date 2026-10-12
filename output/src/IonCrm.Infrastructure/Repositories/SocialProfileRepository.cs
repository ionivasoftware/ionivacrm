using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="ISocialProfileRepository"/>.</summary>
public sealed class SocialProfileRepository : ISocialProfileRepository
{
    private readonly ApplicationDbContext _context;

    public SocialProfileRepository(ApplicationDbContext context) => _context = context;

    public Task<List<SocialProfile>> ListAsync(string language, CancellationToken cancellationToken = default) =>
        _context.SocialProfiles.IgnoreQueryFilters().Where(p => !p.IsDeleted && p.Language == language)
            .OrderBy(p => p.Provider).ToListAsync(cancellationToken);

    public Task<SocialProfile?> GetAsync(SocialProvider provider, string language, CancellationToken cancellationToken = default) =>
        _context.SocialProfiles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Provider == provider && p.Language == language && !p.IsDeleted, cancellationToken);

    public async Task AddAsync(SocialProfile profile, CancellationToken cancellationToken = default)
    {
        await _context.SocialProfiles.AddAsync(profile, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SocialProfile profile, CancellationToken cancellationToken = default)
    {
        profile.UpdatedAt = DateTime.UtcNow;
        if (_context.Entry(profile).State == EntityState.Detached) _context.SocialProfiles.Update(profile);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var deleted = await _context.SocialProfiles.IgnoreQueryFilters().Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }
}
