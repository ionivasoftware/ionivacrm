using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut.Commands.CompleteParasutOAuth;

/// <summary>Handles <see cref="CompleteParasutOAuthCommand"/>: validates the single-use state, exchanges the code, resolves the company via /me and stores the tokens.</summary>
public sealed class CompleteParasutOAuthCommandHandler : IRequestHandler<CompleteParasutOAuthCommand, ParasutOAuthCallbackResult>
{
    private readonly IParasutClient _parasutClient;
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly ITokenProtector _tokenProtector;
    private readonly ILogger<CompleteParasutOAuthCommandHandler> _logger;

    public CompleteParasutOAuthCommandHandler(
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        ITokenProtector tokenProtector,
        ILogger<CompleteParasutOAuthCommandHandler> logger)
    {
        _parasutClient = parasutClient;
        _connectionRepository = connectionRepository;
        _tokenProtector = tokenProtector;
        _logger = logger;
    }

    public async Task<ParasutOAuthCallbackResult> Handle(CompleteParasutOAuthCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.State) || request.State.Length > 200) return ParasutOAuthCallbackResult.Fail(ParasutOAuthFlow.ReasonState);

        var row = await _connectionRepository.GetByOAuthStateHashAsync(ParasutOAuthFlow.HashState(request.State), cancellationToken);
        if (row is null) return ParasutOAuthCallbackResult.Fail(ParasutOAuthFlow.ReasonState);

        var now = DateTime.UtcNow;
        var expired = row.OAuthStateExpiresAt is not { } expires || expires < now;
        var userId = row.OAuthStateUserId;
        var verifier = ParasutOAuthFlow.ReadSecret(_tokenProtector, row.OAuthCodeVerifier);

        // SINGLE USE: state and verifier are cleared whatever the outcome — a replayed callback finds nothing.
        row.OAuthStateHash = null;
        row.OAuthStateExpiresAt = null;
        row.OAuthStateUserId = null;
        row.OAuthCodeVerifier = null;
        row.UpdatedAt = now;
        await _connectionRepository.UpdateAsync(row, cancellationToken);

        if (expired) return ParasutOAuthCallbackResult.Fail(ParasutOAuthFlow.ReasonExpired);
        if (!string.IsNullOrWhiteSpace(request.Error))
            return ParasutOAuthCallbackResult.Fail(request.Error == "access_denied" ? ParasutOAuthFlow.ReasonDenied : ParasutOAuthFlow.ReasonParasut);
        if (string.IsNullOrWhiteSpace(request.Code)) return ParasutOAuthCallbackResult.Fail(ParasutOAuthFlow.ReasonCode);
        if (string.IsNullOrWhiteSpace(verifier)) return ParasutOAuthCallbackResult.Fail(ParasutOAuthFlow.ReasonVerifier);

        ParasutTokenResponse tokens;
        try
        {
            tokens = await _parasutClient.ExchangeCodeAsync(request.Code, verifier, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Paraşüt kod takası başarısız.");
            return ParasutOAuthCallbackResult.Fail(ParasutOAuthFlow.ReasonExchange);
        }

        // The company is no longer typed by hand: /me lists the user's companies. An existing id is kept when it is still among them.
        IReadOnlyList<ParasutMeCompany> companies = Array.Empty<ParasutMeCompany>();
        try
        {
            companies = (await _parasutClient.GetMeAsync(tokens.AccessToken, cancellationToken)).Companies;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Paraşüt /me sorgusu başarısız; satırdaki firma numarası korunur.");
        }

        var companyId = row.CompanyId > 0 && companies.Any(c => c.Id == row.CompanyId) ? row.CompanyId
            : companies.Count > 0 ? companies[0].Id
            : row.CompanyId;
        if (companyId <= 0) return ParasutOAuthCallbackResult.Fail(ParasutOAuthFlow.ReasonCompany);

        row.AuthFlow = ParasutAuthFlow.OAuth2;
        ParasutTokenHelper.ApplyToken(row, tokens, _tokenProtector);
        row.CompanyId = companyId;
        row.CompanyName = companies.FirstOrDefault(c => c.Id == companyId)?.Name ?? row.CompanyName;
        // The new flow never keeps a password — Paraşüt no longer lets applications see it.
        row.Password = string.Empty;
        row.ConnectedAt = now;
        row.ConnectedByUserId = userId;
        row.IsDeleted = false;
        row.UpdatedAt = DateTime.UtcNow;
        await _connectionRepository.UpdateAsync(row, cancellationToken);

        _logger.LogInformation("Paraşüt yetki koduyla bağlandı. Firma={CompanyId} ({CompanyName}), kullanıcı={UserId}", companyId, row.CompanyName, userId);
        return ParasutOAuthCallbackResult.Connected();
    }
}
