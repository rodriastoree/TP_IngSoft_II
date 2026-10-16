using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ISII.Web.Domain.CouponAggregate;

namespace ISII.Web.Infrastructure.Data.Config;

public class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
  public void Configure(EntityTypeBuilder<Coupon> builder)
  {
    builder.ToTable("Coupons", table =>
    {
      // SQLite counts Unicode code points; the domain enforces the UTF-16 length limits.
      table.HasCheckConstraint("CK_Coupons_Code_MaxLength", "length(\"Code\") <= 30");
      table.HasCheckConstraint("CK_Coupons_Dates",
        "typeof(\"CreatedAt\") = 'integer' AND typeof(\"ExpirationDate\") = 'integer' AND " +
        "\"CreatedAt\" BETWEEN 0 AND 3155378975999999999 AND " +
        "\"ExpirationDate\" BETWEEN 0 AND 3155378975999999999 AND " +
        "\"ExpirationDate\" > \"CreatedAt\"");
      table.HasCheckConstraint("CK_Coupons_IsActive", "\"IsActive\" IN (0, 1)");
    });

    builder.HasKey(entity => entity.Id);

    builder.Property(entity => entity.Id)
      .HasValueGenerator<VogenGuidIdValueGenerator<AppDbContext, Coupon, CouponId>>()
      .HasVogenConversion()
      .IsRequired();

    builder.Property(entity => entity.Code)
      .HasMaxLength(30)
      .UseCollation("BINARY")
      .IsRequired();

    builder.HasIndex(entity => entity.Code)
      .IsUnique();

    // SQLite stores decimal as TEXT without rounding; the domain validates its range.
    builder.Property(entity => entity.DiscountPercentage)
      .HasColumnType("TEXT")
      .IsRequired();

    // Ticks preserve full DateTime precision and allow exact chronological CHECKs.
    var utcDateConverter = new ValueConverter<DateTime, long>(
      date => ToUtcTicks(date),
      ticks => new DateTime(ticks, DateTimeKind.Utc));

    builder.Property(entity => entity.ExpirationDate)
      .HasConversion(utcDateConverter)
      .HasColumnType("INTEGER")
      .IsRequired();

    builder.Property(entity => entity.IsActive)
      .IsRequired();

    builder.Property(entity => entity.CreatedAt)
      .HasConversion(utcDateConverter)
      .HasColumnType("INTEGER")
      .IsRequired();
  }

  private static long ToUtcTicks(DateTime date)
  {
    if (date.Kind != DateTimeKind.Utc)
    {
      throw new ArgumentException("Date must be expressed in UTC.", nameof(date));
    }

    return date.Ticks;
  }
}
