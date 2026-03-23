using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Features.Sync.Queries.GetSyncLogs;

/// <summary>
/// Handles <see cref="GetSyncLogsQuery"/> — returns paged sync history.
/// SuperAdmin sees all projects; others are scoped to their project memberships.
/// </summary>
public sealed class GetSyncLogsQueryHandler
    : IRequestHandler<GetSyncLogsQuery, Result<PagedResult<SyncLogDto>>>
{
    private readonly ISyncLogRepository _syncLogRepository;
    private readonly ICurrentUserService _currentUser;

    /// <summary>Initialises a new instance of <see cref="GetSyncLogsQueryHandler"/>.</summary>
    public GetSyncLogsQueryHandler(
        ISyncLogRepository syncLogRepository,
        ICurrentUserService currentUser)
    {
        _syncLogRepository = syncLogRepository;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<SyncLogDto>>> Handle(
        GetSyncLogsQuery request,
        CancellationToken cancellationToken)
    {
        // Non-SuperAdmin users can only see their own project's logs.
        // SECURITY FIX: validate that a caller-supplied projectId belongs to the current user.
        // Without this check, any authenticated user could enumerate any tenant's sync logs
        // by supplying a foreign projectId — a cross-tenant data leak.
        var projectIdFilter = request.ProjectId;
        if (!_currentUser.IsSuperAdmin)
        {
            if (_currentUser.ProjectIds.Count == 0)
                return Result<PagedResult<SyncLogDto>>.Failure("Access denied: no project membership.");

            if (projectIdFilter is null)
            {
                // Default to the user's first project when none is specified
                projectIdFilter = _currentUser.ProjectIds[0];
            }
            else if (!_currentUser.ProjectIds.Contains(projectIdFilter.Value))
            {
                // Caller specified a project they do not belong to — deny
                return Result<PagedResult<SyncLogDto>>.Failure("Access denied to the requested project.");
            }
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, total) = await _syncLogRepository.GetPagedAsync(
            page,
            pageSize,
            projectIdFilter,
            request.Source,
            request.Direction,
            request.Status,
            cancellationToken);

        var dtos = items.Select(log => new SyncLogDto(
            log.Id,
            log.ProjectId,
            log.Source.ToString(),
            log.Direction.ToString(),
            log.EntityType,
            log.EntityId,
            log.Status.ToString(),
            log.ErrorMessage,
            log.RetryCount,
            log.SyncedAt,
            log.CreatedAt,
            log.UpdatedAt)).ToList();

        var result = new PagedResult<SyncLogDto>(dtos, total, page, pageSize);
        return Result<PagedResult<SyncLogDto>>.Success(result);
    }
}
