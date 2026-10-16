namespace ISII.Web.CouponFeatures;

public record CouponRecord(
  Guid Id,
  string Code,
  decimal DiscountPercentage,
  DateTime ExpirationDate,
  bool IsActive,
  DateTime CreatedAt);
