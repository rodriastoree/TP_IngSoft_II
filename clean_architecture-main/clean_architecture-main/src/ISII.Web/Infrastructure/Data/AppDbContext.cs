using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ISII.Web.Domain.CartAggregate;
using ISII.Web.Domain.GuestUserAggregate;
using ISII.Web.Domain.OrderAggregate;
using ISII.Web.Domain.ProductAggregate;

namespace ISII.Web.Infrastructure.Data;
public class AppDbContext(DbContextOptions<AppDbContext> options) : 
  DbContext(options)
{
  public DbSet<Product> Products => Set<Product>();
  public DbSet<Cart> Carts => Set<Cart>();
  public DbSet<CartItem> CartItems => Set<CartItem>();
  public DbSet<GuestUser> GuestUsers => Set<GuestUser>();
  public DbSet<Order> Orders => Set<Order>();
  public DbSet<OrderItem> OrderItems => Set<OrderItem>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
  }

  public override int SaveChanges() =>
        SaveChangesAsync().GetAwaiter().GetResult();
}
