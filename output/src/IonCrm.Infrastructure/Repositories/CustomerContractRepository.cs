using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICustomerContractRepository"/>.
/// All read methods use <c>IgnoreQueryFilters()</c> + a manual <c>!IsDeleted</c>
/// predicate so the repository works in background-job contexts where the
/// global tenant filter would otherwise return zero rows.
/// </summary>
public sealed class CustomerContractRepository : ICustomerContractRepository
{
    private readonly ApplicationDbContext _context;

    /// <summary>Initialises a new instance of <see cref="CustomerContractRepository"/>.</summary>
    public CustomerContractRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<CustomerContract?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.CustomerContracts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CustomerContract?> GetActiveByCustomerIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return await _context.CustomerContracts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => !c.IsDeleted
                     && c.CustomerId == customerId
                     && c.Status == ContractStatus.Active)
            .OrderByDescending(c => c.StartDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerContract>> GetActiveEftContractsDueAsync(
        DateTime today,
        CancellationToken cancellationToken = default)
    {
        return await _context.CustomerContracts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => !c.IsDeleted
                     && c.Status == ContractStatus.Active
                     && c.PaymentType == ContractPaymentType.EftWire
                     && c.NextInvoiceDate != null
                     && c.NextInvoiceDate <= today
                     && (c.EndDate == null || c.EndDate >= today))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CustomerContract> AddAsync(
        CustomerContract contract,
        CancellationToken cancellationToken = default)
    {
        if (contract.Id == Guid.Empty)
            contract.Id = Guid.NewGuid();

        await _context.CustomerContracts.AddAsync(contract, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return contract;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        CustomerContract contract,
        CancellationToken cancellationToken = default)
    {
        contract.UpdatedAt = DateTime.UtcNow;
        _context.CustomerContracts.Update(contract);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
