using FluentValidation;

namespace IonCrm.Application.Migration.Commands.RunMigration;

/// <summary>
/// FluentValidation rules for <see cref="RunMigrationCommand"/>.
/// Validates that project ID and connection string are present before dispatching.
/// </summary>
public sealed class RunMigrationCommandValidator : AbstractValidator<RunMigrationCommand>
{
    /// <summary>Initialises validation rules.</summary>
    public RunMigrationCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .NotEmpty()
            .WithMessage("ProjectId is required. All migrated records will be assigned to this project.");

        RuleFor(x => x.MssqlConnectionString)
            .NotEmpty()
            .WithMessage("MSSQL connection string is required.")
            .MinimumLength(20)
            .WithMessage("Connection string appears too short to be valid.")
            .Must(cs => cs.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
                        cs.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Connection string must contain 'Server=' or 'Data Source=' to be a valid MSSQL connection string.");
    }
}
