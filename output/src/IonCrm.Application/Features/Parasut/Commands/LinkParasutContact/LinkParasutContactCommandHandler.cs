using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut.Commands.LinkParasutContact;

/// <summary>Handles <see cref="LinkParasutContactCommand"/>.</summary>
public sealed class LinkParasutContactCommandHandler
    : IRequestHandler<LinkParasutContactCommand, Result<LinkParasutContactDto>>
{
    private readonly IParasutClient _parasutClient;
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ILogger<LinkParasutContactCommandHandler> _logger;

    public LinkParasutContactCommandHandler(
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        ICustomerRepository customerRepository,
        ILogger<LinkParasutContactCommandHandler> logger)
    {
        _parasutClient = parasutClient;
        _connectionRepository = connectionRepository;
        _customerRepository = customerRepository;
        _logger = logger;
    }

    public async Task<Result<LinkParasutContactDto>> Handle(
        LinkParasutContactCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Load + refresh connection
        var connection = await _connectionRepository.GetByProjectIdAsync(
            request.ProjectId, cancellationToken);

        var (conn, tokenError) = await ParasutTokenHelper.EnsureValidTokenAsync(
            connection, _parasutClient, _connectionRepository, _logger, cancellationToken);
        if (conn is null)
            return Result<LinkParasutContactDto>.Failure(tokenError!);

        // 2. Load customer
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<LinkParasutContactDto>.Failure("Müşteri bulunamadı.");

        try
        {
            // 3. Verify the Paraşüt contact exists by fetching it
            var contactResponse = await _parasutClient.GetContactByIdAsync(
                conn.AccessToken!,
                conn.CompanyId,
                request.ParasutContactId,
                cancellationToken);

            var contactName = contactResponse.Data.Attributes.Name;

            // 4. Persist the link
            customer.ParasutContactId = request.ParasutContactId;
            await _customerRepository.UpdateAsync(customer, cancellationToken);

            _logger.LogInformation(
                "Linked customer {CustomerId} to Paraşüt contact {ParasutId}.",
                request.CustomerId, request.ParasutContactId);

            return Result<LinkParasutContactDto>.Success(new LinkParasutContactDto(
                CustomerId:         request.CustomerId,
                ParasutContactId:   request.ParasutContactId,
                ParasutContactName: contactName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to link customer {CustomerId} to Paraşüt contact {ParasutId}.",
                request.CustomerId, request.ParasutContactId);
            return Result<LinkParasutContactDto>.Failure(
                $"Paraşüt cari bağlantısı kurulamadı: {ex.Message}");
        }
    }
}
