using FastEndpoints;
using ISII.Web.Domain.CouponAggregate;
using ISII.Web.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ISII.Web.CouponFeatures.GetById;

public sealed class GetCouponByIdRequest
{
  public const string Route = "/Coupons/{CouponId}";
  public Guid CouponId { get; init; }
}

public class GetByIdEndpoint(IMediator mediator)
  : Endpoint<GetCouponByIdRequest,
             Results<Ok<CouponRecord>, NotFound, ProblemHttpResult>,
             GetCouponByIdMapper>
{
  public override void Configure()
  {
    Get(GetCouponByIdRequest.Route);
    AllowAnonymous();

    Summary(s =>
    {
      var exampleId = Guid.Parse("12345678-1234-1234-1234-123456789012");
      var exampleCreatedAt = DateTime.UtcNow;

      s.Summary = "Get a coupon by ID";
      s.Description = "Retrieves a coupon by its unique identifier, including expired and inactive coupons.";
      s.ExampleRequest = new GetCouponByIdRequest { CouponId = exampleId };
      s.ResponseExamples[200] = new CouponRecord(
        exampleId,
        "WELCOME10",
        10m,
        exampleCreatedAt.AddDays(30),
        true,
        exampleCreatedAt);
      s.Responses[200] = "Coupon found and returned successfully";
      s.Responses[400] = "Coupon ID must not be empty";
      s.Responses[404] = "Coupon with specified ID not found";
    });

    Tags("Coupons");

    Description(builder => builder
      .Accepts<GetCouponByIdRequest>()
      .Produces<CouponRecord>(200, "application/json")
      .ProducesProblem(400)
      .Produces(404));
  }

  public override async Task<Results<Ok<CouponRecord>, NotFound, ProblemHttpResult>> ExecuteAsync(
    GetCouponByIdRequest request, CancellationToken cancellationToken)
  {
    if (request.CouponId == Guid.Empty)
    {
      return TypedResults.Problem(
        title: "Invalid coupon ID",
        detail: "Coupon ID must not be empty.",
        statusCode: StatusCodes.Status400BadRequest);
    }

    var couponId = CouponId.From(request.CouponId);
    var result = await mediator.Send(new GetCouponQuery(couponId), cancellationToken);

    return result.ToGetByIdResult(Map.FromEntity);
  }
}

public sealed class GetCouponByIdMapper
  : Mapper<GetCouponByIdRequest, CouponRecord, CouponDto>
{
  public override CouponRecord FromEntity(CouponDto e)
    => new(e.Id.Value, e.Code, e.DiscountPercentage, e.ExpirationDate, e.IsActive, e.CreatedAt);
}
