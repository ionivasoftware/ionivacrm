using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Customers.Commands.CreateCustomer;
using IonCrm.Application.Customers.Commands.UpdateCustomer;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Customers;

/// <summary>
/// Tests that verify the CustomerLabel (YuksekPotansiyel/Potansiyel/Notr/Vasat/Kotu)
/// is correctly handled in Create and Update commands.
/// </summary>
public class CustomerLabelTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<CreateCustomerCommandHandler>> _createLoggerMock = new();
    private readonly Mock<ILogger<UpdateCustomerCommandHandler>> _updateLoggerMock = new();

    private static readonly Guid _projectId = Guid.NewGuid();

    private CreateCustomerCommandHandler CreateHandler() => new(
        _customerRepoMock.Object,
        _currentUserMock.Object,
        _createLoggerMock.Object);

    private UpdateCustomerCommandHandler UpdateHandler() => new(
        _customerRepoMock.Object,
        _currentUserMock.Object,
        _updateLoggerMock.Object);

    [Theory]
    [InlineData(CustomerLabel.YuksekPotansiyel)]
    [InlineData(CustomerLabel.Potansiyel)]
    [InlineData(CustomerLabel.Notr)]
    [InlineData(CustomerLabel.Vasat)]
    [InlineData(CustomerLabel.Kotu)]
    public async Task CreateCustomer_WithLabel_LabelStoredInDto(CustomerLabel label)
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _customerRepoMock.Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new CreateCustomerCommand
        {
            ProjectId = _projectId,
            CompanyName = "Labeled Corp",
            Label = label
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Label.Should().Be(label);
    }

    [Fact]
    public async Task CreateCustomer_WithoutLabel_LabelIsNull()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _customerRepoMock.Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new CreateCustomerCommand
        {
            ProjectId = _projectId,
            CompanyName = "No Label Corp",
            Label = null
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Label.Should().BeNull();
    }

    [Fact]
    public async Task UpdateCustomer_ChangesLabel_LabelUpdatedInDto()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var existing = new Customer
        {
            Id = customerId,
            ProjectId = _projectId,
            CompanyName = "Corp",
            Label = CustomerLabel.Notr,
            Status = CustomerStatus.Lead
        };

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _customerRepoMock.Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _customerRepoMock.Setup(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateCustomerCommand
        {
            Id = customerId,
            CompanyName = "Corp",
            Status = CustomerStatus.Lead,
            Label = CustomerLabel.YuksekPotansiyel
        };

        // Act
        var result = await UpdateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Label.Should().Be(CustomerLabel.YuksekPotansiyel);
    }

    [Fact]
    public void CustomerLabel_AllValues_HaveDistinctIntValues()
    {
        // Ensure enum values are unique (prevents accidental duplicate value bugs)
        var values = Enum.GetValues<CustomerLabel>().Cast<int>().ToList();
        values.Should().OnlyHaveUniqueItems("each CustomerLabel must have a distinct integer value");
    }

    [Fact]
    public void CustomerLabel_HasFiveValues()
    {
        // Verify the 5 label values are all present
        var values = Enum.GetValues<CustomerLabel>();
        values.Should().HaveCount(5);
        values.Should().Contain(CustomerLabel.YuksekPotansiyel);
        values.Should().Contain(CustomerLabel.Potansiyel);
        values.Should().Contain(CustomerLabel.Notr);
        values.Should().Contain(CustomerLabel.Vasat);
        values.Should().Contain(CustomerLabel.Kotu);
    }
}
