using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut.Commands.DisconnectParasut;

/// <summary>Handles <see cref="DisconnectParasutCommand"/>.</summary>
public sealed class DisconnectParasutCommandHandler
    : IRequestHandler<DisconnectParasutCommand, Result>
{
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly ILogger<DisconnectParasutCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="DisconnectParasutCommandHandler"/>.</summary>
    public DisconnectParasutCommandHandler(
        IParasutConnectionRepository connectionRepository,
        ILogger<DisconnectParasutCommandHandler> logger)
    {
        _connectionRepository = connectionRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        DisconnectParasutCommand request,
        CancellationToken cancellationToken)
    {
        var connection = await _connectionRepository.GetByProjectIdAsync(
            request.ProjectId, cancellationToken);

        if (connection is null)
            return Result.Failure("Bu proje için Paraşüt bağlantısı bulunamadı.");

        await _connectionRepository.DeleteAsync(connection, cancellationToken);

        _logger.LogInformation(
            "Paraşüt disconnected for project {ProjectId}.", request.ProjectId);

        return Result.Success();
    }
}
