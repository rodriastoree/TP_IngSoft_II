using Ardalis.SharedKernel;
using ISII.Tests.Support;
using ISII.Web.Domain.CouponAggregate;
using ISII.Web.Domain.CouponAggregate.Specifications;
using ISII.Web.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ISII.Tests.Integration.Persistence;

public class CouponPersistenceTests : IAsyncLifetime
{
  // xUnit creates a new test class instance, and therefore a new database, for each case.
  private readonly SqliteTestDatabase _database = new();

  public Task InitializeAsync() => _database.InitializeAsync();

  public Task DisposeAsync() => _database.DisposeAsync();

  [Fact]
  public async Task Migrate_ShouldApplyInitialAndAddCouponWithUniqueBinaryCodeIndex()
  {
    await using var context = _database.CreateContext();
    var expectedMigrations = new[] { "20260829135436_Initial", "20261010023149_AddCoupon" };

    var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();

    Assert.Equal(expectedMigrations, appliedMigrations);
    Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    Assert.Equal(3, await context.Products.CountAsync());

    await context.Database.OpenConnectionAsync();
    await using var command = context.Database.GetDbConnection().CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'Coupons';";
    Assert.Equal(1L, await command.ExecuteScalarAsync());

    command.CommandText = "SELECT \"unique\" FROM pragma_index_list('Coupons') WHERE name = 'IX_Coupons_Code';";
    Assert.Equal(1L, await command.ExecuteScalarAsync());

    command.CommandText = "SELECT coll FROM pragma_index_xinfo('IX_Coupons_Code') WHERE name = 'Code' AND \"key\" = 1;";
    Assert.Equal("BINARY", await command.ExecuteScalarAsync());
  }

  [Fact]
  public async Task ReadRepository_ShouldRecoverAllValuesWithExactDecimalAndUtcTicksFromAnotherContext()
  {
    var coupon = CreateCoupon("ROUNDTRIP");
    await SaveCouponAsync(coupon);

    await using var readContext = _database.CreateContext();
    IReadRepository<Coupon> repository = new EfRepository<Coupon>(readContext);
    var recovered = await repository.FirstOrDefaultAsync(new CouponByIdSpec(coupon.Id));

    Assert.NotNull(recovered);
    AssertCouponMatches(coupon, recovered);
    Assert.Equal(coupon.Id.Value, recovered.Id.Value);
    Assert.Equal(12.345678m, recovered.DiscountPercentage);
  }

  [Fact]
  public async Task ReadRepository_ShouldReturnNullForMissingCouponId()
  {
    await SaveCouponAsync(CreateCoupon("EXISTING"));
    var missingId = CouponId.From(Guid.NewGuid());
    await using var context = _database.CreateContext();
    IReadRepository<Coupon> repository = new EfRepository<Coupon>(context);

    var recovered = await repository.FirstOrDefaultAsync(new CouponByIdSpec(missingId));

    Assert.Null(recovered);
  }

  [Fact]
  public async Task Save_ShouldRejectDuplicateCodeWithUniqueConstraintAndKeepOnlyTheFirstCoupon()
  {
    var firstCoupon = CreateCoupon("DUPLICATE");
    var duplicateCoupon = CreateCoupon("DUPLICATE");
    await SaveCouponAsync(firstCoupon);

    await using (var writeContext = _database.CreateContext())
    {
      IRepository<Coupon> repository = new EfRepository<Coupon>(writeContext);
      var exception = await Assert.ThrowsAsync<DbUpdateException>(() => repository.AddAsync(duplicateCoupon));

      var sqliteException = Assert.IsType<SqliteException>(exception.InnerException);
      Assert.Equal(19, sqliteException.SqliteErrorCode);
      Assert.Equal(2067, sqliteException.SqliteExtendedErrorCode);
    }

    await using var readContext = _database.CreateContext();
    var recovered = Assert.Single(await readContext.Coupons.Where(c => c.Code == "DUPLICATE").ToListAsync());
    AssertCouponMatches(firstCoupon, recovered);
  }

  [Fact]
  public async Task Save_ShouldAllowCodesThatDifferOnlyByCapitalization()
  {
    var upperCaseCoupon = CreateCoupon("PROMO10");
    var lowerCaseCoupon = CreateCoupon("promo10");

    await SaveCouponAsync(upperCaseCoupon);
    await SaveCouponAsync(lowerCaseCoupon);

    await using var context = _database.CreateContext();
    Assert.Equal(2, await context.Coupons.CountAsync());
    Assert.Equal(upperCaseCoupon.Id, (await context.Coupons.SingleAsync(c => c.Code == "PROMO10")).Id);
    Assert.Equal(lowerCaseCoupon.Id, (await context.Coupons.SingleAsync(c => c.Code == "promo10")).Id);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task ReadRepository_ShouldMaterializeExpiredOrInactiveCouponWithoutFiltering(bool expired)
  {
    var coupon = CreateCoupon(expired ? "EXPIRED" : "INACTIVE");
    await SaveCouponAsync(coupon);
    var currentDate = DateTime.UtcNow;
    var createdAt = expired ? currentDate.AddDays(-2) : coupon.CreatedAt;
    var expirationDate = expired ? currentDate.AddDays(-1) : coupon.ExpirationDate;
    var isActive = expired;

    await using (var preparationContext = _database.CreateContext())
    {
      var affectedRows = await preparationContext.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE "Coupons"
        SET "CreatedAt" = {createdAt.Ticks}, "ExpirationDate" = {expirationDate.Ticks}, "IsActive" = {isActive}
        WHERE "Id" = {coupon.Id.Value};
        """);
      Assert.Equal(1, affectedRows);
    }

    await using var readContext = _database.CreateContext();
    IReadRepository<Coupon> repository = new EfRepository<Coupon>(readContext);
    var recovered = await repository.FirstOrDefaultAsync(new CouponByIdSpec(coupon.Id));

    Assert.NotNull(recovered);
    Assert.NotSame(coupon, recovered);
    Assert.Equal(coupon.Id, recovered.Id);
    Assert.Equal(coupon.Code, recovered.Code);
    Assert.Equal(coupon.DiscountPercentage, recovered.DiscountPercentage);
    Assert.Equal(createdAt.Ticks, recovered.CreatedAt.Ticks);
    Assert.Equal(expirationDate.Ticks, recovered.ExpirationDate.Ticks);
    Assert.Equal(DateTimeKind.Utc, recovered.CreatedAt.Kind);
    Assert.Equal(DateTimeKind.Utc, recovered.ExpirationDate.Kind);
    Assert.Equal(isActive, recovered.IsActive);
    Assert.False(recovered.IsValidAt(currentDate));
  }

  [Theory]
  [InlineData("Id")]
  [InlineData("Code")]
  [InlineData("DiscountPercentage")]
  [InlineData("ExpirationDate")]
  [InlineData("IsActive")]
  [InlineData("CreatedAt")]
  public async Task Sqlite_ShouldRejectNullInRequiredColumns(string column)
  {
    var coupon = CreateCoupon("NOTNULL");
    await SaveCouponAsync(coupon);
    FormattableString update = column switch
    {
      "Id" => $"UPDATE \"Coupons\" SET \"Id\" = NULL WHERE \"Id\" = {coupon.Id.Value};",
      "Code" => $"UPDATE \"Coupons\" SET \"Code\" = NULL WHERE \"Id\" = {coupon.Id.Value};",
      "DiscountPercentage" => $"UPDATE \"Coupons\" SET \"DiscountPercentage\" = NULL WHERE \"Id\" = {coupon.Id.Value};",
      "ExpirationDate" => $"UPDATE \"Coupons\" SET \"ExpirationDate\" = NULL WHERE \"Id\" = {coupon.Id.Value};",
      "IsActive" => $"UPDATE \"Coupons\" SET \"IsActive\" = NULL WHERE \"Id\" = {coupon.Id.Value};",
      "CreatedAt" => $"UPDATE \"Coupons\" SET \"CreatedAt\" = NULL WHERE \"Id\" = {coupon.Id.Value};",
      _ => throw new ArgumentOutOfRangeException(nameof(column))
    };

    await AssertUpdateRejectedAsync(coupon, update, 1299);
  }

  [Fact]
  public async Task Sqlite_ShouldRejectCodeLongerThanThirtyCharacters()
  {
    var coupon = CreateCoupon("MAXLENGTH");
    await SaveCouponAsync(coupon);
    var invalidCode = new string('A', 31);

    await AssertUpdateRejectedAsync(coupon,
      $"UPDATE \"Coupons\" SET \"Code\" = {invalidCode} WHERE \"Id\" = {coupon.Id.Value};", 275);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public async Task Sqlite_ShouldRejectExpirationNotLaterThanCreation(int tickOffset)
  {
    var coupon = CreateCoupon("DATEORDER");
    await SaveCouponAsync(coupon);
    var expirationTicks = coupon.CreatedAt.Ticks + tickOffset;

    await AssertUpdateRejectedAsync(coupon,
      $"UPDATE \"Coupons\" SET \"ExpirationDate\" = {expirationTicks} WHERE \"Id\" = {coupon.Id.Value};", 275);
  }

  [Theory]
  [InlineData("CreatedAt", -1L)]
  [InlineData("CreatedAt", 3155378976000000000L)]
  [InlineData("ExpirationDate", -1L)]
  [InlineData("ExpirationDate", 3155378976000000000L)]
  public async Task Sqlite_ShouldRejectTicksOutsideDateTimeRange(string column, long invalidTicks)
  {
    var coupon = CreateCoupon("TICKRANGE");
    await SaveCouponAsync(coupon);
    FormattableString update = column switch
    {
      "CreatedAt" => $"UPDATE \"Coupons\" SET \"CreatedAt\" = {invalidTicks} WHERE \"Id\" = {coupon.Id.Value};",
      "ExpirationDate" => $"UPDATE \"Coupons\" SET \"ExpirationDate\" = {invalidTicks} WHERE \"Id\" = {coupon.Id.Value};",
      _ => throw new ArgumentOutOfRangeException(nameof(column))
    };

    await AssertUpdateRejectedAsync(coupon, update, 275);
  }

  [Theory]
  [InlineData("CreatedAt")]
  [InlineData("ExpirationDate")]
  public async Task Sqlite_ShouldRejectDatesStoredAsNonIntegerText(string column)
  {
    var coupon = CreateCoupon("DATETYPE");
    await SaveCouponAsync(coupon);
    const string invalidTicks = "not-an-integer";
    FormattableString update = column switch
    {
      "CreatedAt" => $"UPDATE \"Coupons\" SET \"CreatedAt\" = {invalidTicks} WHERE \"Id\" = {coupon.Id.Value};",
      "ExpirationDate" => $"UPDATE \"Coupons\" SET \"ExpirationDate\" = {invalidTicks} WHERE \"Id\" = {coupon.Id.Value};",
      _ => throw new ArgumentOutOfRangeException(nameof(column))
    };

    await AssertUpdateRejectedAsync(coupon, update, 275);
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(2)]
  public async Task Sqlite_ShouldRejectActiveStateOtherThanZeroOrOne(int invalidState)
  {
    var coupon = CreateCoupon("ACTIVESTATE");
    await SaveCouponAsync(coupon);

    await AssertUpdateRejectedAsync(coupon,
      $"UPDATE \"Coupons\" SET \"IsActive\" = {invalidState} WHERE \"Id\" = {coupon.Id.Value};", 275);
  }

  [Fact]
  public async Task Database_ShouldIsolateFilesAndCleanOnlyItsOwnTemporaryDirectory()
  {
    var otherDatabase = new SqliteTestDatabase();
    try
    {
      await otherDatabase.InitializeAsync();
      var otherDirectory = Path.GetDirectoryName(otherDatabase.DatabasePath)!;
      Assert.True(Path.IsPathFullyQualified(_database.DatabasePath));
      Assert.NotEqual(_database.DatabasePath, otherDatabase.DatabasePath);
      await SaveCouponAsync(CreateCoupon("ISOLATED"));

      await using (var context = otherDatabase.CreateContext())
      {
        var connectionString = new SqliteConnectionStringBuilder(context.Database.GetConnectionString());
        Assert.Equal(otherDatabase.DatabasePath, connectionString.DataSource);
        Assert.Equal(SqliteOpenMode.ReadWriteCreate, connectionString.Mode);
        Assert.False(connectionString.Pooling);
        Assert.Equal(0, await context.Coupons.CountAsync());
      }

      await otherDatabase.DisposeAsync();

      Assert.False(Directory.Exists(otherDirectory));
      Assert.True(File.Exists(_database.DatabasePath));
      await using var readContext = _database.CreateContext();
      Assert.Equal(1, await readContext.Coupons.CountAsync());
    }
    finally
    {
      await otherDatabase.DisposeAsync();
    }
  }

  private async Task SaveCouponAsync(Coupon coupon)
  {
    await using var context = _database.CreateContext();
    IRepository<Coupon> repository = new EfRepository<Coupon>(context);
    await repository.AddAsync(coupon);
    await repository.SaveChangesAsync();
  }

  private async Task AssertUpdateRejectedAsync(Coupon coupon, FormattableString update, int extendedErrorCode)
  {
    await using (var context = _database.CreateContext())
    {
      var exception = await Assert.ThrowsAsync<SqliteException>(
        () => context.Database.ExecuteSqlInterpolatedAsync(update));

      Assert.Equal(19, exception.SqliteErrorCode);
      Assert.Equal(extendedErrorCode, exception.SqliteExtendedErrorCode);
    }

    await using var readContext = _database.CreateContext();
    AssertCouponMatches(coupon, await readContext.Coupons.SingleAsync());
  }

  private static void AssertCouponMatches(Coupon expected, Coupon actual)
  {
    Assert.NotSame(expected, actual);
    Assert.Equal(expected.Id, actual.Id);
    Assert.Equal(expected.Code, actual.Code);
    Assert.Equal(expected.DiscountPercentage, actual.DiscountPercentage);
    Assert.Equal(expected.ExpirationDate.Ticks, actual.ExpirationDate.Ticks);
    Assert.Equal(expected.CreatedAt.Ticks, actual.CreatedAt.Ticks);
    Assert.Equal(expected.IsActive, actual.IsActive);
    Assert.Equal(DateTimeKind.Utc, actual.ExpirationDate.Kind);
    Assert.Equal(DateTimeKind.Utc, actual.CreatedAt.Kind);
  }

  private static Coupon CreateCoupon(string code)
    => Coupon.Create(CouponId.From(Guid.NewGuid()), code, 12.345678m, DateTime.UtcNow.Date.AddDays(30).AddTicks(123));
}
