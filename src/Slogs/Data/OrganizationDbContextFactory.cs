using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Slogs.Data;

public sealed class OrganizationDbContextFactory : IDesignTimeDbContextFactory<OrganizationDbContext>
{
    public OrganizationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SLOGS_MIGRATION_CONNECTION_STRING")
            ?? "Host=localhost;Port=54329;Database=slogs;Username=slogs;Password=slogs_dev_password";
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseNpgsql(connectionString)
            .UseOpenIddict()
            .Options;
        return new OrganizationDbContext(options);
    }
}
