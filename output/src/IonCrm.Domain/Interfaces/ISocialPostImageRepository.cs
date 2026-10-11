using IonCrm.Domain.Entities;

namespace IonCrm.Domain.Interfaces;

/// <summary>Blob store for <see cref="SocialPostImage"/>. Rows are hard-deleted: a soft-deleted blob frees nothing.</summary>
public interface ISocialPostImageRepository
{
    Task AddAsync(SocialPostImage image, CancellationToken cancellationToken = default);

    Task<SocialPostImage?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stored template card for (post, language), if the publisher already fetched one.</summary>
    Task<SocialPostImage?> FindCardAsync(Guid postId, string language, CancellationToken cancellationToken = default);

    /// <summary>Removes the row; returns false when it did not exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
