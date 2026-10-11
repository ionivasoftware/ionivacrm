using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Interfaces;

/// <summary>Publishing pipeline state: per-post bookkeeping and per-(post × account) jobs.</summary>
public interface ISocialPublishRepository
{
    Task<SocialPublishPost?> GetPostAsync(Guid postId, CancellationToken cancellationToken = default);

    /// <summary>Returns the existing row for <c>candidate.PostId</c> or inserts the candidate.</summary>
    Task<SocialPublishPost> GetOrAddPostAsync(SocialPublishPost candidate, CancellationToken cancellationToken = default);

    Task UpdatePostAsync(SocialPublishPost post, CancellationToken cancellationToken = default);

    Task<List<SocialPublishJob>> GetJobsForPostAsync(Guid postId, CancellationToken cancellationToken = default);

    Task<SocialPublishJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Inserts unless a job for (PostId, AccountId) already exists; returns false when it did.</summary>
    Task<bool> TryAddJobAsync(SocialPublishJob job, CancellationToken cancellationToken = default);

    Task UpdateJobAsync(SocialPublishJob job, CancellationToken cancellationToken = default);

    /// <summary>Pending/Failed jobs whose backoff has elapsed, plus InProgress jobs older than <paramref name="staleBefore"/>.</summary>
    Task<List<SocialPublishJob>> GetDueJobsAsync(DateTime now, DateTime staleBefore, CancellationToken cancellationToken = default);

    /// <summary>Posts not yet stamped back to EMS (candidates for the stamp check).</summary>
    Task<List<SocialPublishPost>> GetUnstampedPostsAsync(CancellationToken cancellationToken = default);

    Task<List<SocialPublishJob>> ListJobsAsync(Guid? postId, SocialPublishJobStatus? status, int limit, CancellationToken cancellationToken = default);

    Task<List<SocialPublishPost>> ListPostsAsync(int limit, CancellationToken cancellationToken = default);

    Task<Dictionary<SocialPublishJobStatus, int>> CountJobsByStatusAsync(CancellationToken cancellationToken = default);
}
