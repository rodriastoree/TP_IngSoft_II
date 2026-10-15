using ISII.Web.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ISII.Tests.Support;

public sealed class SqliteTestDatabase : IAsyncLifetime
{
  private const string DirectoryPrefix = "ISII-Coupon-Persistence-";
  private readonly string _temporaryRoot;
  private readonly string _directoryPath;
  private readonly DbContextOptions<AppDbContext> _options;
  private readonly List<AppDbContext> _contexts = [];
  private bool _disposed;

  public SqliteTestDatabase()
  {
    _temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
    _directoryPath = Directory.CreateTempSubdirectory(DirectoryPrefix).FullName;
    DatabasePath = Path.Combine(_directoryPath, "coupon.sqlite3");
    ValidateOwnedPaths();

    var connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = DatabasePath,
      Mode = SqliteOpenMode.ReadWriteCreate,
      Pooling = false
    }.ToString();

    _options = new DbContextOptionsBuilder<AppDbContext>()
      .UseSqlite(connectionString)
      .Options;
  }

  public string DatabasePath { get; }

  public async Task InitializeAsync()
  {
    try
    {
      await using var context = CreateContext();
      await context.Database.MigrateAsync();
    }
    catch
    {
      await DisposeAsync();
      throw;
    }
  }

  public AppDbContext CreateContext()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
    var context = new AppDbContext(_options);
    _contexts.Add(context);
    return context;
  }

  public async Task DisposeAsync()
  {
    if (_disposed) return;

    foreach (var context in _contexts)
    {
      await context.DisposeAsync();
    }

    ValidateOwnedPaths();
    foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
    {
      File.Delete(DatabasePath + suffix);
    }

    // A non-recursive delete refuses to remove unexpected files or subdirectories.
    Directory.Delete(_directoryPath, recursive: false);
    _disposed = true;
  }

  private void ValidateOwnedPaths()
  {
    var directory = Path.GetFullPath(_directoryPath);
    var database = Path.GetFullPath(DatabasePath);
    if (!string.Equals(Path.GetDirectoryName(directory), _temporaryRoot, StringComparison.OrdinalIgnoreCase)
      || !Path.GetFileName(directory).StartsWith(DirectoryPrefix, StringComparison.Ordinal)
      || !string.Equals(Path.GetDirectoryName(database), directory, StringComparison.OrdinalIgnoreCase)
      || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
    {
      throw new InvalidOperationException("The test database must stay inside its owned temporary directory.");
    }
  }
}
