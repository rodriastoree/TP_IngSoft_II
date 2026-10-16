using ISII.Web.Domain.CouponAggregate;
using ISII.Web.Domain.CouponAggregate.Specifications;

namespace ISII.Web.CouponFeatures.GetById;

public record GetCouponQuery(CouponId CouponId) : IQuery<Result<CouponDto>>;

public class GetCouponHandler(IReadRepository<Coupon> repository)
  : IQueryHandler<GetCouponQuery, Result<CouponDto>>
{
  public async ValueTask<Result<CouponDto>> Handle(GetCouponQuery request, CancellationToken cancellationToken)
  {
    var spec = new CouponByIdSpec(request.CouponId);
    var entity = await repository.FirstOrDefaultAsync(spec, cancellationToken);
    if (entity == null) return Result.NotFound();

    return new CouponDto(
      entity.Id,
      entity.Code,
      entity.DiscountPercentage,
      entity.ExpirationDate,
      entity.IsActive,
      entity.CreatedAt);
  }
}
