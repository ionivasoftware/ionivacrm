using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Customers.Commands.CreateCustomerContract;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.UpdateContractPaymentType;

public sealed class UpdateContractPaymentTypeCommandHandler
    : IRequestHandler<UpdateContractPaymentTypeCommand, Result<CustomerContractDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ICustomerContractRepository _contractRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UpdateContractPaymentTypeCommandHandler> _logger;

    public UpdateContractPaymentTypeCommandHandler(
        ICustomerRepository customerRepository,
        ICustomerContractRepository contractRepository,
        ICurrentUserService currentUser,
        ILogger<UpdateContractPaymentTypeCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _contractRepository = contractRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<CustomerContractDto>> Handle(
        UpdateContractPaymentTypeCommand request,
        CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<CustomerContractDto>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<CustomerContractDto>.Failure("Bu müşteriye erişim yetkiniz yok.");

        var contract = await _contractRepository.GetActiveByCustomerIdAsync(customer.Id, cancellationToken);
        if (contract is null)
            return Result<CustomerContractDto>.Failure("Aktif sözleşme bulunamadı.");

        if (contract.PaymentType == request.PaymentType)
            return Result<CustomerContractDto>.Success(
                CreateCustomerContractCommandHandler.MapToDto(contract));

        var oldType = contract.PaymentType;
        contract.PaymentType = request.PaymentType;

        if (request.PaymentType == ContractPaymentType.EftWire)
        {
            // Switching to EFT — schedule next invoice if not already set.
            if (!contract.NextInvoiceDate.HasValue)
            {
                var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
                var next = contract.StartDate;
                while (next < today && (contract.EndDate is null || next < contract.EndDate))
                {
                    var n = next.AddMonths(1);
                    int daysInMonth = DateTime.DaysInMonth(n.Year, n.Month);
                    int targetDay = Math.Min(contract.StartDate.Day, daysInMonth);
                    next = DateTime.SpecifyKind(new DateTime(n.Year, n.Month, targetDay), DateTimeKind.Utc);
                }
                contract.NextInvoiceDate = next;
            }
        }
        else
        {
            // Switching to CreditCard — iyzico handles billing, no draft invoices.
            contract.NextInvoiceDate = null;
        }

        await _contractRepository.UpdateAsync(contract, cancellationToken);

        _logger.LogInformation(
            "Contract {ContractId} payment type changed from {Old} to {New} for customer {CustomerId}.",
            contract.Id, oldType, request.PaymentType, customer.Id);

        return Result<CustomerContractDto>.Success(
            CreateCustomerContractCommandHandler.MapToDto(contract));
    }
}
