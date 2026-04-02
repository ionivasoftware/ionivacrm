using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Net;

namespace IonCrm.Application.Customers.Commands.PushCustomerToRezerval;

/// <summary>Handles <see cref="PushCustomerToRezervalCommand"/>.</summary>
public sealed class PushCustomerToRezervalCommandHandler
    : IRequestHandler<PushCustomerToRezervalCommand, Result<PushCustomerToRezervalDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ISaasBClient _saasBClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<PushCustomerToRezervalCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="PushCustomerToRezervalCommandHandler"/>.</summary>
    public PushCustomerToRezervalCommandHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ISaasBClient saasBClient,
        ICurrentUserService currentUser,
        ILogger<PushCustomerToRezervalCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _saasBClient        = saasBClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<PushCustomerToRezervalDto>> Handle(
        PushCustomerToRezervalCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Load customer
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<PushCustomerToRezervalDto>.Failure("Müşteri bulunamadı.");

        // 2. Tenant authorization
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<PushCustomerToRezervalDto>.Failure("Bu müşteriye erişim yetkiniz yok.");

        // 3. Resolve project RezervAl API key
        var project = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        var rezervAlApiKey = project?.RezervAlApiKey;

        if (string.IsNullOrWhiteSpace(rezervAlApiKey))
            return Result<PushCustomerToRezervalDto>.Failure(
                "Bu proje için RezervAl API anahtarı tanımlanmamış.");

        // 4. Decode optional logo bytes
        byte[]? logoBytes = null;
        if (!string.IsNullOrWhiteSpace(request.LogoBase64))
        {
            try
            {
                logoBytes = Convert.FromBase64String(request.LogoBase64);
            }
            catch (FormatException)
            {
                return Result<PushCustomerToRezervalDto>.Failure(
                    "Logo base64 formatı geçersiz.");
            }
        }

        // 5. Build form data from command
        var formData = new RezervalCompanyFormData
        {
            Name              = request.Name,
            Title             = request.Title,
            Phone             = request.Phone,
            Email             = request.Email,
            TaxUnit           = request.TaxUnit,
            TaxNumber         = request.TaxNumber,
            TCNo              = request.TCNo,
            IsPersonCompany   = request.IsPersonCompany,
            Address           = request.Address,
            Language          = request.Language,
            CountryPhoneCode  = request.CountryPhoneCode,
            ExperationDate    = request.ExperationDate,
            AdminNameSurname  = request.AdminNameSurname,
            AdminLoginName    = request.AdminLoginName,
            AdminPassword     = request.AdminPassword,
            AdminEmail        = request.AdminEmail,
            AdminPhone        = request.AdminPhone,
            LogoBytes         = logoBytes,
            LogoFileName      = request.LogoFileName
        };

        // 6. Determine create vs. update from LegacyId
        //    LegacyId "REZV-{n}" → update existing Rezerval company
        //    Any other value (null, numeric, "PC-...", etc.) → create new
        bool isUpdate = customer.LegacyId?.StartsWith("REZV-", StringComparison.OrdinalIgnoreCase) == true;

        if (isUpdate)
        {
            // Extract numeric Rezerval company ID from "REZV-{n}"
            var rawId = customer.LegacyId!["REZV-".Length..];
            if (!int.TryParse(rawId, out var existingCompanyId))
            {
                return Result<PushCustomerToRezervalDto>.Failure(
                    $"LegacyId '{customer.LegacyId}' içindeki RezervAl company ID geçersiz.");
            }

            try
            {
                await _saasBClient.UpdateRezervalCompanyAsync(
                    existingCompanyId, formData, rezervAlApiKey, cancellationToken);

                _logger.LogInformation(
                    "Updated RezervAl company {RezervalId} for customer {CustomerId} ({Name}).",
                    existingCompanyId, customer.Id, customer.CompanyName);

                return Result<PushCustomerToRezervalDto>.Success(
                    new PushCustomerToRezervalDto(existingCompanyId, customer.LegacyId!, WasCreated: false));
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Company no longer exists in Rezerval — fall through to create a new one.
                _logger.LogWarning(
                    "RezervAl company {RezervalId} not found (404) for customer {CustomerId}. Falling back to create.",
                    existingCompanyId, customer.Id);
                customer.LegacyId = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "RezervAl company update failed for customer {CustomerId} (RezervalId {RezervalId}).",
                    customer.Id, existingCompanyId);
                return Result<PushCustomerToRezervalDto>.Failure(
                    $"RezervAl'de firma güncellenemedi: {ex.Message}");
            }
        }
        // Create new company in RezervAl (either first-time or 404-fallback from update above)
        RezervalCreateCompanyResponse createResponse;
        try
        {
            createResponse = await _saasBClient.CreateRezervalCompanyAsync(
                formData, rezervAlApiKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RezervAl company create failed for customer {CustomerId}.",
                customer.Id);
            return Result<PushCustomerToRezervalDto>.Failure(
                $"RezervAl'de firma oluşturulamadı: {ex.Message}");
        }

        // Persist the new LegacyId so future calls trigger update instead of create
        var newLegacyId = $"REZV-{createResponse.CompanyId}";
        customer.LegacyId  = newLegacyId;
        customer.UpdatedAt = DateTime.UtcNow;
        await _customerRepository.UpdateAsync(customer, cancellationToken);

        _logger.LogInformation(
            "Created RezervAl company {RezervalId} for customer {CustomerId} ({Name}). LegacyId set to '{LegacyId}'.",
            createResponse.CompanyId, customer.Id, customer.CompanyName, newLegacyId);

        return Result<PushCustomerToRezervalDto>.Success(
            new PushCustomerToRezervalDto(createResponse.CompanyId, newLegacyId, WasCreated: true));
    }
}
