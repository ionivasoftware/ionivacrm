using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut.Commands.LinkParasutContact;

/// <summary>Handles <see cref="LinkParasutContactCommand"/>.</summary>
public sealed class LinkParasutContactCommandHandler
    : IRequestHandler<LinkParasutContactCommand, Result<LinkParasutContactDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ILogger<LinkParasutContactCommandHandler> _logger;

    public LinkParasutContactCommandHandler(
        ICustomerRepository customerRepository,
        ILogger<LinkParasutContactCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _logger = logger;
    }

    public async Task<Result<LinkParasutContactDto>> Handle(
        LinkParasutContactCommand request,
        CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<LinkParasutContactDto>.Failure("Müşteri bulunamadı.");

        try
        {
            customer.ParasutContactId = request.ParasutContactId;
            await _customerRepository.UpdateAsync(customer, cancellationToken);

            _logger.LogInformation(
                "Linked customer {CustomerId} to Paraşüt contact {ParasutId}.",
                request.CustomerId, request.ParasutContactId);

            return Result<LinkParasutContactDto>.Success(new LinkParasutContactDto(
                CustomerId:         request.CustomerId,
                ParasutContactId:   request.ParasutContactId,
                ParasutContactName: request.ParasutContactName ?? request.ParasutContactId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to link customer {CustomerId} to Paraşüt contact {ParasutId}.",
                request.CustomerId, request.ParasutContactId);
            return Result<LinkParasutContactDto>.Failure(
                $"Kayıt edilemedi: {ex.Message}");
        }
    }
}
