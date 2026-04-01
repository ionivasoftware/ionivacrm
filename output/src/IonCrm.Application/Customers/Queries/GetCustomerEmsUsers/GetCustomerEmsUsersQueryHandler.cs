using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Queries.GetCustomerEmsUsers;

/// <summary>Handles <see cref="GetCustomerEmsUsersQuery"/>.</summary>
public sealed class GetCustomerEmsUsersQueryHandler
    : IRequestHandler<GetCustomerEmsUsersQuery, Result<List<EmsCompanyUserDto>>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ISaasAClient _saasAClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<GetCustomerEmsUsersQueryHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="GetCustomerEmsUsersQueryHandler"/>.</summary>
    public GetCustomerEmsUsersQueryHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ISaasAClient saasAClient,
        ICurrentUserService currentUser,
        ILogger<GetCustomerEmsUsersQueryHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _saasAClient        = saasAClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<List<EmsCompanyUserDto>>> Handle(
        GetCustomerEmsUsersQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Load customer
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<List<EmsCompanyUserDto>>.Failure("Müşteri bulunamadı.");

        // 2. Tenant authorization
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<List<EmsCompanyUserDto>>.Failure("Bu müşteriye erişim yetkiniz yok.");

        // 3. Verify the customer is EMS-sourced and extract numeric company ID.
        //    LegacyId formats:
        //      "3"        → plain numeric (EMS CRM canonical)
        //      "SAASA-3"  → prefixed (older sync format)
        //      "PC-123"   → PotentialCustomer — NOT an EMS company user source
        if (string.IsNullOrEmpty(customer.LegacyId)
            || customer.LegacyId.StartsWith("PC-", StringComparison.OrdinalIgnoreCase))
        {
            return Result<List<EmsCompanyUserDto>>.Failure(
                "Bu müşteri EMS kaynakli değil. EMS kullanıcıları yalnızca EMS kaynaklı müşteriler için sorgulanabilir.");
        }

        string rawId = customer.LegacyId.StartsWith("SAASA-", StringComparison.OrdinalIgnoreCase)
            ? customer.LegacyId["SAASA-".Length..]
            : customer.LegacyId;

        if (!int.TryParse(rawId, out var emsCompanyId))
        {
            return Result<List<EmsCompanyUserDto>>.Failure(
                "Bu müşteri EMS kaynakli değil. EMS kullanıcıları yalnızca EMS kaynaklı müşteriler için sorgulanabilir.");
        }

        // 4. Resolve project EMS API key (null → SaasAClient falls back to DI-configured default)
        var project    = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        var emsApiKey  = project?.EmsApiKey;

        // 5. Call EMS API
        List<Common.Models.ExternalApis.EmsCompanyUser> emsUsers;
        try
        {
            emsUsers = await _saasAClient.GetCompanyUsersAsync(emsApiKey, emsCompanyId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EMS GetCompanyUsers failed for customer {CustomerId} (EMS company {EmsId}).",
                customer.Id, emsCompanyId);
            return Result<List<EmsCompanyUserDto>>.Failure(
                $"EMS'ten kullanıcı listesi alınamadı: {ex.Message}");
        }

        // 6. Map to DTOs
        var dtos = emsUsers
            .Select(u => new EmsCompanyUserDto(
                u.UserId,
                u.Name,
                u.Surname,
                u.Email,
                u.Role,
                u.LoginName,
                u.Password))
            .ToList();

        _logger.LogInformation(
            "Fetched {Count} EMS users for customer {CustomerId} (EMS company {EmsId}).",
            dtos.Count, customer.Id, emsCompanyId);

        return Result<List<EmsCompanyUserDto>>.Success(dtos);
    }
}
