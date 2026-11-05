using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut.Commands.StartParasutOAuth;

/// <summary>Handles <see cref="StartParasutOAuthCommand"/>. Only the global connection (<c>ProjectId = null</c>) uses the new flow.</summary>
public sealed class StartParasutOAuthCommandHandler : IRequestHandler<StartParasutOAuthCommand, Result<StartParasutOAuthDto>>
{
    public const string NotConfiguredMessage = "Paraşüt'ün yeni bağlantı yöntemi sunucuda yapılandırılmamış (Parasut__OAuth__ClientId ve Parasut__OAuth__RedirectUri).";

    private readonly IParasutClient _parasutClient;
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly ITokenProtector _tokenProtector;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<StartParasutOAuthCommandHandler> _logger;

    public StartParasutOAuthCommandHandler(
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        ITokenProtector tokenProtector,
        ICurrentUserService currentUser,
        ILogger<StartParasutOAuthCommandHandler> logger)
    {
        _parasutClient = parasutClient;
        _connectionRepository = connectionRepository;
        _tokenProtector = tokenProtector;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<StartParasutOAuthDto>> Handle(StartParasutOAuthCommand request, CancellationToken cancellationToken)
    {
        if (!_parasutClient.OAuth2Available)
            return Result<StartParasutOAuthDto>.Failure(NotConfiguredMessage);

        // One global row: a legacy (password) row is reused and only flips to OAuth2 when the callback succeeds.
        var row = await _connectionRepository.GetGlobalAsync(cancellationToken);
        var isNew = row is null;
        row ??= new ParasutConnection { ProjectId = null, AuthFlow = ParasutAuthFlow.OAuth2 };

        var state = ParasutOAuthFlow.NewState();
        var verifier = ParasutOAuthFlow.NewCodeVerifier();
        var challenge = ParasutOAuthFlow.CodeChallenge(verifier);
        var now = DateTime.UtcNow;

        row.OAuthStateHash = ParasutOAuthFlow.HashState(state);
        row.OAuthStateExpiresAt = now.Add(ParasutOAuthSettings.StateLifetime);
        row.OAuthStateUserId = _currentUser.UserId == Guid.Empty ? null : _currentUser.UserId;
        row.OAuthCodeVerifier = _tokenProtector.Protect(verifier);
        row.UpdatedAt = now;

        if (isNew) await _connectionRepository.AddAsync(row, cancellationToken);
        else await _connectionRepository.UpdateAsync(row, cancellationToken);

        _logger.LogInformation("Paraşüt yetkilendirmesi başlatıldı (yetki kodu + PKCE). Kullanıcı={UserId}", _currentUser.UserId);
        return Result<StartParasutOAuthDto>.Success(new StartParasutOAuthDto(_parasutClient.BuildAuthorizationUrl(state, challenge), row.OAuthStateExpiresAt.Value));
    }
}
