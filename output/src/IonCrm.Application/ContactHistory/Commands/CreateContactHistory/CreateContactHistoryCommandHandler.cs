using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.ContactHistory.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.ContactHistory.Commands.CreateContactHistory;

/// <summary>Handles <see cref="CreateContactHistoryCommand"/>.</summary>
public class CreateContactHistoryCommandHandler : IRequestHandler<CreateContactHistoryCommand, Result<ContactHistoryDto>>
{
    private readonly IContactHistoryRepository _contactHistoryRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CreateContactHistoryCommandHandler> _logger;

    public CreateContactHistoryCommandHandler(
        IContactHistoryRepository contactHistoryRepository,
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser,
        ILogger<CreateContactHistoryCommandHandler> logger)
    {
        _contactHistoryRepository = contactHistoryRepository;
        _customerRepository = customerRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<ContactHistoryDto>> Handle(CreateContactHistoryCommand request, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<ContactHistoryDto>.Failure("Customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<ContactHistoryDto>.Failure("Access denied to this customer.");

        var userId = _currentUser.UserId == Guid.Empty ? (Guid?)null : _currentUser.UserId;

        var history = new Domain.Entities.ContactHistory
        {
            CustomerId = request.CustomerId,
            ProjectId = customer.ProjectId,
            Type = request.Type,
            Subject = request.Subject,
            Content = request.Content,
            Outcome = request.Outcome,
            ContactedAt = request.ContactedAt,
            CreatedByUserId = userId
        };

        await _contactHistoryRepository.AddAsync(history, cancellationToken);

        _logger.LogInformation("ContactHistory {Id} created for customer {CustomerId}", history.Id, history.CustomerId);

        return Result<ContactHistoryDto>.Success(history.ToDto());
    }
}
