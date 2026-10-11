using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="ISocialPostImageRepository"/>.</summary>
public sealed class SocialPostImageRepository : ISocialPostImageRepository
{
    private readonly ApplicationDbContext _context;

    public SocialPostImageRepository(ApplicationDbContext context) => _context = context;

    public async Task AddAsync(SocialPostImage image, CancellationToken cancellationToken = default)
    {
        await _context.SocialPostImages.AddAsync(image, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<SocialPostImage?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.SocialPostImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<SocialPostImage?> FindCardAsync(Guid postId, string language, CancellationToken cancellationToken = default) =>
        _context.SocialPostImages.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.PostId == postId && i.Kind == "Card" && i.Language == language && !i.IsDeleted)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var deleted = await _context.SocialPostImages.Where(i => i.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }
}
