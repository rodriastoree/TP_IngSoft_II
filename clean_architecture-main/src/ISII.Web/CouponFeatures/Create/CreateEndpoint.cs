using FastEndpoints;
using ISII.Web.Domain.CouponAggregate;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ISII.Web.CouponFeatures.Create;

public sealed class CreateCouponRequest
{
  public string Code { get; init; } = string.Empty;
  public decimal DiscountPercentage { get; init; }
  public DateTime ExpirationDate { get; init; }
}

public class CreateEndpoint(IRepository<Coupon> repository)
  : Endpoint<CreateCouponRequest, Created<CouponRecord>>
{
  private readonly IRepository<Coupon> _repository = repository;

  public override void Configure()
  {
    Post("/Coupons");
    AllowAnonymous();

    Summary(s =>
    {
      var exampleCreatedAt = DateTime.UtcNow;
      var exampleExpirationDate = exampleCreatedAt.AddDays(30);

      s.Summary = "Create a new coupon";
      s.Description = "Creates a coupon with the specified code, discount percentage, and future UTC expiration date. " +
        "The identifier, creation date, and initial active state are generated internally.";
      s.ExampleRequest = new CreateCouponRequest
      {
        Code = "WELCOME10",
        DiscountPercentage = 10m,
        ExpirationDate = exampleExpirationDate
      };
      s.ResponseExamples[201] = new CouponRecord(
        Guid.Parse("12345678-1234-1234-1234-123456789012"),
        "WELCOME10",
        10m,
        exampleExpirationDate,
        true,
        exampleCreatedAt);
      s.Responses[201] = "Coupon created successfully";
    });

    Tags("Coupons");

    Description(builder => builder
      .Accepts<CreateCouponRequest>("application/json")
      .Produces<CouponRecord>(201, "application/json"));
  }

  public override async Task<Created<CouponRecord>> ExecuteAsync(
    CreateCouponRequest request, CancellationToken cancellationToken)
  {
    var couponId = CouponId.From(Guid.NewGuid());
    var coupon = Coupon.Create(couponId, request.Code, request.DiscountPercentage, request.ExpirationDate);

    var persistedCoupon = await _repository.AddAsync(coupon, cancellationToken);
    await _repository.SaveChangesAsync(cancellationToken);

    var response = new CouponRecord(
      persistedCoupon.Id.Value,
      persistedCoupon.Code,
      persistedCoupon.DiscountPercentage,
      persistedCoupon.ExpirationDate,
      persistedCoupon.IsActive,
      persistedCoupon.CreatedAt);

    return TypedResults.Created($"/Coupons/{persistedCoupon.Id.Value}", response);
  }
}
