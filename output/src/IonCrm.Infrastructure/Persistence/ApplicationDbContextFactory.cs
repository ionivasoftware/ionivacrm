using IonCrm.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace IonCrm.Infrastructure.Persistence;

/// <summary>
/// EF Core design-time factory used by <c>dotnet ef migrations</c>.
/// Reads the connection string from environment variable or falls back to a placeholder
/// that allows the migration to be generated without a live database.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    /// <inheritdoc />
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Try to read from appsettings.json in the API startup project
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Database=ioncrm_dev;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        // Use a SuperAdmin stub so all tenant query filters are bypassed during migrations
        return new ApplicationDbContext(optionsBuilder.Options, new DesignTimeCurrentUserService());
    }

    /// <summary>
    /// Stub implementation of <see cref="ICurrentUserService"/> for design-time use only.
    /// Acts as a SuperAdmin so no tenant query filters block schema inspection.
    /// </summary>
    private sealed class DesignTimeCurrentUserService : ICurrentUserService
    {
        public Guid UserId => Guid.Empty;
        public string Email => string.Empty;
        public bool IsSuperAdmin => true;   // Bypass all tenant filters
        public List<Guid> ProjectIds => new();
        public bool IsAuthenticated => false;
        public string? GetRoleForProject(Guid projectId) => null;
    }
}
