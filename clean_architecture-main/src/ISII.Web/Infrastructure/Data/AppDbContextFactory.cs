using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ISII.Web.Infrastructure.Data;

/// <summary>
/// Design-time factory for creating SQLite DbContext instances for EF Core tools.
/// Runtime configuration is registered in InfrastructureServiceExtensions.
///
/// Migrations are automatically APPLIED at runtime via MigrateAsync() in MiddlewareConfig.cs.
///
/// Uses the same default SQLite connection string as appsettings.json.
/// Relative DataSource paths are resolved against the process working directory.
/// Set ConnectionStrings__AppDb to a SQLite connection string with an absolute
/// DataSource path to share a database across different working directories.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
  public AppDbContext CreateDbContext(string[] args)
  {
    var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

    // Try to get connection string from environment variable (ASP.NET Core format)
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__AppDb")
                          ?? "DataSource=ISII.db;Cache=Shared";

    optionsBuilder.UseSqlite(connectionString);

    return new AppDbContext(optionsBuilder.Options);
  }
}
