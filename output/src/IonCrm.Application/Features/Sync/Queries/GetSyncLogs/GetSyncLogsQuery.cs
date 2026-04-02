using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Features.Sync.Queries.GetSyncLogs;

/// <summary>
/// Returns a paged list of sync log entries.
/// SuperAdmin sees all entries across all projects.
/// ProjectAdmin/other roles see only their own project's logs.
/// </summary>
public record GetSyncLogsQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? ProjectId = null,
    SyncSource? Source = null,
    SyncDirection? Direction = null,
    SyncStatus? Status = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null
) : IRequest<Result<PagedResult<SyncLogDto>>>;
