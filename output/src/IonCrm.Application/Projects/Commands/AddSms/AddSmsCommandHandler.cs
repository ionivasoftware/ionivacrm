using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Projects.Commands.AddSms;

/// <summary>Handles <see cref="AddSmsCommand"/>.</summary>
public sealed class AddSmsCommandHandler : IRequestHandler<AddSmsCommand, Result<AddSmsDto>>
{
    private readonly IProjectRepository _projectRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<AddSmsCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="AddSmsCommandHandler"/>.</summary>
    public AddSmsCommandHandler(
        IProjectRepository projectRepository,
        ICurrentUserService currentUser,
        ILogger<AddSmsCommandHandler> logger)
    {
        _projectRepository = projectRepository;
        _currentUser       = currentUser;
        _logger            = logger;
    }

    /// <inheritdoc />
    public async Task<Result<AddSmsDto>> Handle(AddSmsCommand request, CancellationToken cancellationToken)
    {
        // 1. Validate count
        if (request.Count <= 0)
            return Result<AddSmsDto>.Failure("SMS adedi 0'dan büyük olmalıdır.");

        // 2. Load project
        var project = await _projectRepository.GetByIdAsync(request.CompanyId, cancellationToken);
        if (project is null)
            return Result<AddSmsDto>.Failure("Şirket bulunamadı.");

        // 3. Access check: must be SuperAdmin or a member of this project
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(project.Id))
            return Result<AddSmsDto>.Failure("Access denied. Bu şirkete erişim yetkiniz yok.");

        // 4. Add credits
        var previousCount = project.SmsCount;
        project.SmsCount  += request.Count;
        project.UpdatedAt  = DateTime.UtcNow;

        try
        {
            await _projectRepository.UpdateAsync(project, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to add SMS credits for project {ProjectId}. Inner: {Inner}",
                project.Id, ex.InnerException?.Message);
            return Result<AddSmsDto>.Failure("SMS kredisi kaydedilemedi: " + ex.InnerException?.Message ?? ex.Message);
        }

        _logger.LogInformation(
            "Added {Added} SMS credits to project {ProjectId}. Previous: {Previous}, New: {New}.",
            request.Count, project.Id, previousCount, project.SmsCount);

        return Result<AddSmsDto>.Success(new AddSmsDto(
            CompanyId: project.Id,
            SmsCount:  project.SmsCount,
            Added:     request.Count));
    }
}
