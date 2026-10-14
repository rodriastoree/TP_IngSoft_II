namespace ISII.Web.Domain.CouponAggregate.Specifications;

public class CouponByIdSpec : Specification<Coupon>
{
  public CouponByIdSpec(CouponId couponId) =>
    Query
      .Where(coupon => coupon.Id == couponId);
}
