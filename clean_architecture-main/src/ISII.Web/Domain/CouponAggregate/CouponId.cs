using Vogen;

namespace ISII.Web.Domain.CouponAggregate;

[ValueObject<Guid>]
public readonly partial struct CouponId
{
  private static Validation Validate(Guid value)
      => value != Guid.Empty ? Validation.Ok : Validation.Invalid("CouponId cannot be empty.");
}
