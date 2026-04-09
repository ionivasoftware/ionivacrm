using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Customers.Commands.CreateCustomerContract;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Queries.GetActiveContractByCustomerId;

/// <summary>Handles <see cref="GetActiveContractByCustomerIdQuery"/>.</summary>
public sealed class GetActiveContractByCustomerIdQueryHandler
    : IRequestHandler<GetActiveContractByCustomerIdQuery, Result<CustomerContractDto?>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ICustomerContractRepository _contractRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<GetActiveContractByCustomerIdQueryHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="GetActiveContractByCustomerIdQueryHandler"/>.</summary>
    public GetActiveContractByCustomerIdQueryHandler(
        ICustomerRepository customerRepository,
        ICustomerContractRepository contractRepository,
        ICurrentUserService currentUser,
        ILogger<GetActiveContractByCustomerIdQueryHandler> logger)
    {
        _customerRepository = customerRepository;
        _contractRepository = contractRepository;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerContractDto?>> Handle(
        GetActiveContractByCustomerIdQuery request,
        CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<CustomerContractDto?>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<CustomerContractDto?>.Failure("Bu müşteriye erişim yetkiniz yok.");

        try
        {
            var contract = await _contractRepository.GetActiveByCustomerIdAsync(customer.Id, cancellationToken);

            return Result<CustomerContractDto?>.Success(
                contract is null ? null : CreateCustomerContractCommandHandler.MapToDto(contract));
        }
        catch (Exception ex) when (IsMissingTable(ex))
        {
            // PostgreSQL 42P01 = undefined_table. The CustomerContracts table hasn't been
            // created yet (startup bootstrap may have failed on an earlier block). Treat as
            // "no active contract" so the customer detail page still renders, and log loudly
            // so we notice the missing table in Railway logs.
            _logger.LogError(ex,
                "CustomerContracts table is missing while looking up active contract for customer {CustomerId}. " +
                "Check the startup bootstrap logs for the failing block.",
                customer.Id);
            return Result<CustomerContractDto?>.Success(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to load active contract for customer {CustomerId}.",
                customer.Id);
            return Result<CustomerContractDto?>.Failure(
                $"Aktif sözleşme okunamadı: {ex.GetBaseException().Message}");
        }
    }

    /// <summary>
    /// Detects PostgreSQL "undefined_table" (42P01) errors without taking a hard dependency
    /// on Npgsql in the Application layer. Walks the inner-exception chain because EF Core
    /// usually wraps the underlying Npgsql exception.
    /// </summary>
    private static bool IsMissingTable(Exception ex)
    {
        for (var e = (Exception?)ex; e is not null; e = e.InnerException)
        {
            // Match by SqlState property reflectively (PostgresException.SqlState = "42P01")
            // OR fall back to a substring match on the message — robust against version
            // changes where the property name might differ.
            var sqlState = e.GetType().GetProperty("SqlState")?.GetValue(e) as string;
            if (sqlState == "42P01")
                return true;

            if (e.Message.Contains("42P01", StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
