using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.TransferLead;

/// <summary>
/// Handles <see cref="TransferLeadCommand"/>.
/// Validates that the source is a Lead and the target is an active customer,
/// delegates the atomic transfer + soft-delete to the repository.
/// </summary>
public class TransferLeadCommandHandler : IRequestHandler<TransferLeadCommand, Result>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<TransferLeadCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="TransferLeadCommandHandler"/>.</summary>
    public TransferLeadCommandHandler(
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser,
        ILogger<TransferLeadCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(TransferLeadCommand request, CancellationToken cancellationToken)
    {
        // ── 1. Validate Lead ─────────────────────────────────────────────────────
        var lead = await _customerRepository.GetByIdAsync(request.LeadId, cancellationToken);
        if (lead is null)
            return Result.Failure("Lead customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(lead.ProjectId))
            return Result.Failure("Access denied to this lead customer.");

        if (lead.Status != CustomerStatus.Lead)
            return Result.Failure(
                $"Customer {request.LeadId} is not a Lead. Current status: {lead.Status}.");

        // ── 2. Validate Target ───────────────────────────────────────────────────
        var target = await _customerRepository.GetByIdAsync(request.TargetCustomerId, cancellationToken);
        if (target is null)
            return Result.Failure("Target customer not found.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(target.ProjectId))
            return Result.Failure("Access denied to this target customer.");

        if (target.Status == CustomerStatus.Lead)
            return Result.Failure(
                $"Target customer {request.TargetCustomerId} is also a Lead. Target must be an active customer.");

        // ── 3. Same tenant check ─────────────────────────────────────────────────
        if (lead.ProjectId != target.ProjectId)
            return Result.Failure("Lead and target customer must belong to the same project.");

        // ── 4. Self-transfer guard ───────────────────────────────────────────────
        if (request.LeadId == request.TargetCustomerId)
            return Result.Failure("Lead and target customer cannot be the same record.");

        // ── 5. Perform atomic transfer ───────────────────────────────────────────
        try
        {
            await _customerRepository.TransferLeadAsync(request.LeadId, request.TargetCustomerId, cancellationToken);

            _logger.LogInformation(
                "Lead {LeadId} transferred to customer {TargetId} by user {UserId} in project {ProjectId}",
                request.LeadId, request.TargetCustomerId, _currentUser.UserId, lead.ProjectId);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Transfer of lead {LeadId} to customer {TargetId} failed. {InnerMessage}",
                request.LeadId, request.TargetCustomerId, ex.InnerException?.Message);

            return Result.Failure("Transfer failed due to a database error. Please try again.");
        }
    }
}
