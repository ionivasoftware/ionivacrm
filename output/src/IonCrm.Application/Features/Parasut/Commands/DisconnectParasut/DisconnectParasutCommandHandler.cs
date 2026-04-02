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
        // Strict lookup — do not use GetEffectiveConnectionAsync here to avoid accidentally
        // soft-deleting the global connection when disconnecting a project.
        var connection = request.ProjectId.HasValue
            ? await _connectionRepository.GetByProjectIdAsync(request.ProjectId.Value, cancellationToken)
            : await _connectionRepository.GetGlobalAsync(cancellationToken);

        if (connection is null)
        {
            var notFound = request.ProjectId.HasValue
                ? "Bu proje için Paraşüt bağlantısı bulunamadı."
                : "Global Paraşüt bağlantısı bulunamadı.";
            return Result.Failure(notFound);
        }

        await _connectionRepository.DeleteAsync(connection, cancellationToken);

        var target = request.ProjectId.HasValue
            ? $"project {request.ProjectId}"
            : "global";

        _logger.LogInformation(
            "Paraşüt disconnected for {Target}.", target);

        return Result.Success();
    }
}
