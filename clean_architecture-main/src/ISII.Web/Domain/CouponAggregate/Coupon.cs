using Ardalis.GuardClauses;

namespace ISII.Web.Domain.CouponAggregate;

public class Coupon : EntityBase<Coupon, CouponId>, IAggregateRoot
{
  public Coupon(CouponId id, string code, decimal discountPercentage, DateTime expirationDate)
  {
    // Vogen rejects access to Value on an uninitialized identifier.
    _ = id.Value;
    Guard.Against.NullOrWhiteSpace(code, nameof(code));
    Guard.Against.InvalidInput(code, nameof(code), value => value.Length >= 3 && value.Length <= 30,
      "Coupon code must contain between 3 and 30 characters.");
    Guard.Against.InvalidInput(discountPercentage, nameof(discountPercentage), value => value > 0 && value <= 100,
      "Discount percentage must be greater than zero and must not exceed 100.");
    ValidateUtc(expirationDate, nameof(expirationDate));

    var createdAt = DateTime.UtcNow;
    Guard.Against.InvalidInput(expirationDate, nameof(expirationDate), value => value > createdAt,
      "Expiration date must be later than creation date.");

    Id = id;
    Code = code;
    DiscountPercentage = discountPercentage;
    ExpirationDate = expirationDate;
    IsActive = true;
    CreatedAt = createdAt;
  }

  public static Coupon Create(CouponId id, string code, decimal discountPercentage, DateTime expirationDate)
    => new Coupon(id, code, discountPercentage, expirationDate);

  public string Code { get; private set; }
  public decimal DiscountPercentage { get; private set; }
  public DateTime ExpirationDate { get; private set; }
  public bool IsActive { get; private set; }
  public DateTime CreatedAt { get; private set; }

  public bool IsValidAt(DateTime currentDate)
  {
    ValidateUtc(currentDate, nameof(currentDate));
    return IsActive && currentDate < ExpirationDate;
  }

  private static void ValidateUtc(DateTime value, string parameterName)
  {
    Guard.Against.InvalidInput(value, parameterName, date => date.Kind == DateTimeKind.Utc,
      "Date must be expressed in UTC.");
  }
}
