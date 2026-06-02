using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetLinkedParasutContactIds;

/// <summary>Handles <see cref="GetLinkedParasutContactIdsQuery"/>.</summary>
public sealed class GetLinkedParasutContactIdsQueryHandler
    : IRequestHandler<GetLinkedParasutContactIdsQuery, Result<IReadOnlyList<string>>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;

    /// <summary>Initialises a new instance.</summary>
    public GetLinkedParasutContactIdsQueryHandler(
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser)
    {
        _customerRepository = customerRepository;
        _currentUser        = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> Handle(
        GetLinkedParasutContactIdsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(request.ProjectId))
            return Result<IReadOnlyList<string>>.Failure("Bu projeye erişim yetkiniz yok.");

        var ids = await _customerRepository.GetLinkedParasutContactIdsAsync(
            request.ProjectId, cancellationToken);

        return Result<IReadOnlyList<string>>.Success(ids);
    }
}
