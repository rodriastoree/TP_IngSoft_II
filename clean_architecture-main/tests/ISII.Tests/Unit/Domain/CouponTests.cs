using System.Globalization;
using ISII.Web.Domain.CouponAggregate;

namespace ISII.Tests.Unit.Domain;

public class CouponTests
{
  [Fact]
  public void Constructor_ShouldCreateActiveCouponWithProvidedValuesAndUtcCreationTime()
  {
    var id = CreateCouponId();
    const string code = "WELCOME10";
    const decimal discountPercentage = 12.345678m;
    var expirationDate = DateTime.UtcNow.AddDays(30);
    var beforeCreation = DateTime.UtcNow;

    var coupon = new Coupon(id, code, discountPercentage, expirationDate);

    var afterCreation = DateTime.UtcNow;
    Assert.Equal(id, coupon.Id);
    Assert.Equal(code, coupon.Code);
    Assert.Equal(discountPercentage, coupon.DiscountPercentage);
    Assert.Equal(expirationDate, coupon.ExpirationDate);
    Assert.Equal(DateTimeKind.Utc, coupon.ExpirationDate.Kind);
    Assert.True(coupon.IsActive);
    Assert.Equal(DateTimeKind.Utc, coupon.CreatedAt.Kind);
    Assert.InRange(coupon.CreatedAt, beforeCreation, afterCreation);
  }

  [Fact]
  public void Create_ShouldCreateActiveCouponWithProvidedValuesAndUtcCreationTime()
  {
    var id = CreateCouponId();
    const string code = "WELCOME10";
    const decimal discountPercentage = 12.345678m;
    var expirationDate = DateTime.UtcNow.AddDays(30);
    var beforeCreation = DateTime.UtcNow;

    var coupon = Coupon.Create(id, code, discountPercentage, expirationDate);

    var afterCreation = DateTime.UtcNow;
    Assert.Equal(id, coupon.Id);
    Assert.Equal(code, coupon.Code);
    Assert.Equal(discountPercentage, coupon.DiscountPercentage);
    Assert.Equal(expirationDate, coupon.ExpirationDate);
    Assert.Equal(DateTimeKind.Utc, coupon.ExpirationDate.Kind);
    Assert.True(coupon.IsActive);
    Assert.Equal(DateTimeKind.Utc, coupon.CreatedAt.Kind);
    Assert.InRange(coupon.CreatedAt, beforeCreation, afterCreation);
  }

  [Fact]
  public void Create_ShouldRejectNullCode()
  {
    var id = CreateCouponId();
    var expirationDate = DateTime.UtcNow.AddDays(30);

    var exception = Assert.Throws<ArgumentNullException>(
      () => Coupon.Create(id, null!, 10m, expirationDate));

    Assert.Equal("code", exception.ParamName);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("AB")]
  [InlineData("1234567890123456789012345678901")]
  public void Create_ShouldRejectInvalidCode(string code)
  {
    var id = CreateCouponId();
    var expirationDate = DateTime.UtcNow.AddDays(30);

    var exception = Assert.Throws<ArgumentException>(
      () => Coupon.Create(id, code, 10m, expirationDate));

    Assert.Equal("code", exception.ParamName);
  }

  [Theory]
  [InlineData("ABC")]
  [InlineData("123456789012345678901234567890")]
  [InlineData(" welcome10 ")]
  [InlineData("WeLcOmE10")]
  public void Create_ShouldPreserveValidCodeWithoutNormalization(string code)
  {
    var id = CreateCouponId();
    var expirationDate = DateTime.UtcNow.AddDays(30);

    var coupon = Coupon.Create(id, code, 10m, expirationDate);

    Assert.Equal(code, coupon.Code);
  }

  [Fact]
  public void Create_ShouldAcceptCodeWithThreeUtf16CodeUnitsAndTwoUnicodeCodePoints()
  {
    var id = CreateCouponId();
    const string code = "\U0001F600A";
    var expirationDate = DateTime.UtcNow.AddDays(30);

    var coupon = Coupon.Create(id, code, 10m, expirationDate);

    Assert.Equal(3, code.Length);
    Assert.Equal(code, coupon.Code);
  }

  [Theory]
  [InlineData("-1")]
  [InlineData("0")]
  [InlineData("100.01")]
  [InlineData("101")]
  public void Create_ShouldRejectInvalidDiscountPercentage(string percentage)
  {
    var id = CreateCouponId();
    var discountPercentage = decimal.Parse(percentage, CultureInfo.InvariantCulture);
    var expirationDate = DateTime.UtcNow.AddDays(30);

    var exception = Assert.Throws<ArgumentException>(
      () => Coupon.Create(id, "WELCOME10", discountPercentage, expirationDate));

    Assert.Equal("discountPercentage", exception.ParamName);
  }

  [Theory]
  [InlineData("0.01")]
  [InlineData("12.345678")]
  [InlineData("100")]
  public void Create_ShouldPreserveValidDiscountPercentageWithoutRounding(string percentage)
  {
    var id = CreateCouponId();
    var discountPercentage = decimal.Parse(percentage, CultureInfo.InvariantCulture);
    var expirationDate = DateTime.UtcNow.AddDays(30);

    var coupon = Coupon.Create(id, "WELCOME10", discountPercentage, expirationDate);

    Assert.Equal(discountPercentage, coupon.DiscountPercentage);
  }

  [Fact]
  public void Create_ShouldRejectPastExpirationDate()
  {
    var id = CreateCouponId();
    var expirationDate = DateTime.UtcNow.AddDays(-1);

    var exception = Assert.Throws<ArgumentException>(
      () => Coupon.Create(id, "WELCOME10", 10m, expirationDate));

    Assert.Equal("expirationDate", exception.ParamName);
  }

  [Theory]
  [InlineData(DateTimeKind.Local)]
  [InlineData(DateTimeKind.Unspecified)]
  public void Create_ShouldRejectNonUtcExpirationDate(DateTimeKind kind)
  {
    var id = CreateCouponId();
    var expirationDate = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), kind);

    var exception = Assert.Throws<ArgumentException>(
      () => Coupon.Create(id, "WELCOME10", 10m, expirationDate));

    Assert.Equal("expirationDate", exception.ParamName);
  }

  [Fact]
  public void IsValidAt_ShouldReturnTrueBeforeExpiration()
  {
    var coupon = CreateValidCoupon();
    var currentDate = coupon.ExpirationDate.AddTicks(-1);

    var isValid = coupon.IsValidAt(currentDate);

    Assert.True(isValid);
  }

  [Fact]
  public void IsValidAt_ShouldReturnFalseExactlyAtExpiration()
  {
    var coupon = CreateValidCoupon();

    var isValid = coupon.IsValidAt(coupon.ExpirationDate);

    Assert.False(isValid);
  }

  [Fact]
  public void IsValidAt_ShouldReturnFalseAfterExpiration()
  {
    var coupon = CreateValidCoupon();
    var currentDate = coupon.ExpirationDate.AddTicks(1);

    var isValid = coupon.IsValidAt(currentDate);

    Assert.False(isValid);
  }

  [Fact]
  public void IsValidAt_ShouldNotRequireQueryDateToBeAfterCreation()
  {
    var coupon = CreateValidCoupon();
    var currentDate = coupon.CreatedAt.AddTicks(-1);

    var isValid = coupon.IsValidAt(currentDate);

    Assert.True(isValid);
  }

  [Theory]
  [InlineData(DateTimeKind.Local)]
  [InlineData(DateTimeKind.Unspecified)]
  public void IsValidAt_ShouldRejectNonUtcQueryDate(DateTimeKind kind)
  {
    var coupon = CreateValidCoupon();
    var currentDate = DateTime.SpecifyKind(coupon.ExpirationDate.AddTicks(-1), kind);

    var exception = Assert.Throws<ArgumentException>(() => coupon.IsValidAt(currentDate));

    Assert.Equal("currentDate", exception.ParamName);
  }

  private static CouponId CreateCouponId()
    => CouponId.From(Guid.Parse("12345678-1234-1234-1234-123456789012"));

  private static Coupon CreateValidCoupon()
    => Coupon.Create(CreateCouponId(), "WELCOME10", 10m, DateTime.UtcNow.AddDays(30));
}
