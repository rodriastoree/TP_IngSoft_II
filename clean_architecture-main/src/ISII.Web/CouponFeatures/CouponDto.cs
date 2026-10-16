using ISII.Web.Domain.CouponAggregate;

namespace ISII.Web.CouponFeatures;

public record CouponDto(
  CouponId Id,
  string Code,
  decimal DiscountPercentage,
  DateTime ExpirationDate,
  bool IsActive,
  DateTime CreatedAt);
