using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="ISocialPublishRepository"/>. Used by the background publisher (no HTTP context) → IgnoreQueryFilters.</summary>
public sealed class SocialPublishRepository : ISocialPublishRepository
{
    private readonly ApplicationDbContext _context;

    public SocialPublishRepository(ApplicationDbContext context) => _context = context;

    private IQueryable<SocialPublishPost> Posts => _context.SocialPublishPosts.IgnoreQueryFilters().Where(p => !p.IsDeleted);
    private IQueryable<SocialPublishJob> Jobs => _context.SocialPublishJobs.IgnoreQueryFilters().Where(j => !j.IsDeleted);

    public Task<SocialPublishPost?> GetPostAsync(Guid postId, CancellationToken cancellationToken = default) =>
        Posts.FirstOrDefaultAsync(p => p.PostId == postId, cancellationToken);

    public async Task<SocialPublishPost> GetOrAddPostAsync(SocialPublishPost candidate, CancellationToken cancellationToken = default)
    {
        var existing = await GetPostAsync(candidate.PostId, cancellationToken);
        if (existing is not null) return existing;
        await _context.SocialPublishPosts.AddAsync(candidate, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return candidate;
    }

    public async Task UpdatePostAsync(SocialPublishPost post, CancellationToken cancellationToken = default)
    {
        post.UpdatedAt = DateTime.UtcNow;
        if (_context.Entry(post).State == EntityState.Detached) _context.SocialPublishPosts.Update(post);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<List<SocialPublishJob>> GetJobsForPostAsync(Guid postId, CancellationToken cancellationToken = default) =>
        Jobs.Where(j => j.PostId == postId).OrderBy(j => j.CreatedAt).ToListAsync(cancellationToken);

    public Task<SocialPublishJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default) =>
        Jobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public async Task<bool> TryAddJobAsync(SocialPublishJob job, CancellationToken cancellationToken = default)
    {
        var exists = await Jobs.AnyAsync(j => j.PostId == job.PostId && j.AccountId == job.AccountId, cancellationToken);
        if (exists) return false;
        await _context.SocialPublishJobs.AddAsync(job, cancellationToken);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Unique (PostId, AccountId) index won the race against another instance — that is the point of it.
            _context.Entry(job).State = EntityState.Detached;
            return false;
        }
    }

    public async Task UpdateJobAsync(SocialPublishJob job, CancellationToken cancellationToken = default)
    {
        job.UpdatedAt = DateTime.UtcNow;
        if (_context.Entry(job).State == EntityState.Detached) _context.SocialPublishJobs.Update(job);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<List<SocialPublishJob>> GetDueJobsAsync(DateTime now, DateTime staleBefore, CancellationToken cancellationToken = default) =>
        Jobs.Where(j =>
                j.Status == SocialPublishJobStatus.Pending ||
                (j.Status == SocialPublishJobStatus.Failed && (j.NextAttemptAt == null || j.NextAttemptAt <= now)) ||
                (j.Status == SocialPublishJobStatus.InProgress && j.StartedAt != null && j.StartedAt < staleBefore))
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<List<SocialPublishPost>> GetUnstampedPostsAsync(CancellationToken cancellationToken = default) =>
        Posts.Where(p => p.StampedAt == null).OrderBy(p => p.ScheduledFor).ToListAsync(cancellationToken);

    public Task<List<SocialPublishJob>> ListJobsAsync(Guid? postId, SocialPublishJobStatus? status, int limit, CancellationToken cancellationToken = default)
    {
        var q = Jobs;
        if (postId is { } pid) q = q.Where(j => j.PostId == pid);
        if (status is { } st) q = q.Where(j => j.Status == st);
        return q.OrderByDescending(j => j.UpdatedAt).Take(limit).ToListAsync(cancellationToken);
    }

    public Task<List<SocialPublishPost>> ListPostsAsync(int limit, CancellationToken cancellationToken = default) =>
        Posts.OrderByDescending(p => p.ScheduledFor).ThenByDescending(p => p.FirstSeenAt).Take(limit).ToListAsync(cancellationToken);

    public async Task<Dictionary<SocialPublishJobStatus, int>> CountJobsByStatusAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Jobs.GroupBy(j => j.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Key, r => r.Count);
    }
}
