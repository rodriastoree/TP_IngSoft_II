using ISII.Web.Domain.CouponAggregate;
using Vogen;

namespace ISII.Tests.Unit.Domain;

public class CouponIdTests
{
  [Fact]
  public void From_ShouldPreserveProvidedGuid()
  {
    var value = Guid.Parse("12345678-1234-1234-1234-123456789012");

    var couponId = CouponId.From(value);

    Assert.Equal(value, couponId.Value);
  }

  [Fact]
  public void From_ShouldRejectEmptyGuid()
  {
    var value = Guid.Empty;

    Assert.Throws<ValueObjectValidationException>(() => CouponId.From(value));
  }
}
