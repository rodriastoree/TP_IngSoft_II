using ISII.Web.Configurations;
using ISII.Web.Domain.CouponAggregate;
using ISII.Web.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ISII.Tests.Support;

public sealed class CouponWebApplicationFactory(SqliteTestDatabase database) : WebApplicationFactory<Program>
{
  public const string SimulatedFailureMessage = "COUPON_HTTP_TEST_INTERNAL_FAILURE";
  private readonly string _databasePath = Path.GetFullPath(database.DatabasePath);
  private readonly CouponSaveFailureInterceptor _saveFailure = new();

  public int SaveFailureTriggerCount => _saveFailure.TriggerCount;

  public async Task EnableSaveFailureAsync()
  {
    await VerifyDatabaseAsync();
    _saveFailure.Arm();
  }

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment(Environments.Production);
    builder.ConfigureServices(services =>
    {
      services.RemoveAll<AppDbContext>();
      services.RemoveAll<DbContextOptions<AppDbContext>>();
      services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
      services.PostConfigure<DatabaseOptions>(options => options.RecreateOnStartup = false);

      services.AddDbContext<AppDbContext>((provider, options) =>
      {
        // This guard also runs when startup resolves the context for migrations.
        VerifyPath(_databasePath);
        Assert.True(File.Exists(_databasePath));
        Assert.True(provider.GetRequiredService<IHostEnvironment>().IsProduction());
        Assert.False(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.RecreateOnStartup);

        var connectionString = new SqliteConnectionStringBuilder
        {
          DataSource = _databasePath,
          Mode = SqliteOpenMode.ReadWriteCreate,
          Pooling = false
        }.ToString();

        options.UseSqlite(connectionString);
        options.AddInterceptors(provider.GetRequiredService<EventDispatchInterceptor>(), _saveFailure);
      });
    });
  }

  public async Task VerifyDatabaseAsync()
  {
    await using var scope = Services.CreateAsyncScope();
    Assert.True(scope.ServiceProvider.GetRequiredService<IHostEnvironment>().IsProduction());
    Assert.False(scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.RecreateOnStartup);

    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    Assert.True(context.Database.IsSqlite());
    var connectionString = new SqliteConnectionStringBuilder(context.Database.GetConnectionString());
    VerifyPath(connectionString.DataSource);
    Assert.Equal(SqliteOpenMode.ReadWriteCreate, connectionString.Mode);
    Assert.False(connectionString.Pooling);

    var connection = context.Database.GetDbConnection();
    VerifyPath(connection.DataSource);
    await context.Database.OpenConnectionAsync();
    await using (var command = connection.CreateCommand())
    {
      command.CommandText = "PRAGMA database_list;";
      await using var reader = await command.ExecuteReaderAsync();
      var mainDatabaseFound = false;
      while (await reader.ReadAsync())
      {
        if (reader.GetString(1) != "main") continue;
        VerifyPath(reader.GetString(2));
        mainDatabaseFound = true;
      }
      Assert.True(mainDatabaseFound);
    }

    var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
    Assert.Equal(new[] { "20260829135436_Initial", "20261010023149_AddCoupon" }, appliedMigrations);
    Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    _ = await context.Coupons.CountAsync();
  }

  private void VerifyPath(string path)
  {
    Assert.True(Path.IsPathFullyQualified(path));
    Assert.True(string.Equals(_databasePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase),
      "The application must use exactly this case's temporary SQLite file.");
  }

  private sealed class CouponSaveFailureInterceptor : SaveChangesInterceptor
  {
    private int _armed;
    private int _triggerCount;

    public int TriggerCount => Volatile.Read(ref _triggerCount);

    public void Arm() => Interlocked.Exchange(ref _armed, 1);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
      ThrowIfArmed(eventData.Context);
      return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
      InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
      ThrowIfArmed(eventData.Context);
      return ValueTask.FromResult(result);
    }

    private void ThrowIfArmed(DbContext? context)
    {
      if (context is AppDbContext
        && context.ChangeTracker.Entries<Coupon>().Any(entry => entry.State == EntityState.Added)
        && Interlocked.Exchange(ref _armed, 0) == 1)
      {
        Interlocked.Increment(ref _triggerCount);
        throw new InvalidOperationException(SimulatedFailureMessage);
      }
    }
  }
}
