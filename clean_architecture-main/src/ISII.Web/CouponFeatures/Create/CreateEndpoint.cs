using FastEndpoints;
using FluentValidation;
using ISII.Web.Domain.CouponAggregate;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ISII.Web.CouponFeatures.Create;

public sealed class CreateCouponRequest
{
  public string Code { get; init; } = string.Empty;
  public decimal DiscountPercentage { get; init; }
  public DateTime ExpirationDate { get; init; }
}

public class CreateEndpoint(IRepository<Coupon> repository)
  : Endpoint<CreateCouponRequest, Results<Created<CouponRecord>, ProblemHttpResult>>
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
      s.Responses[400] = "Invalid coupon data";
      s.Responses[409] = "A coupon with the specified code already exists";
    });

    Tags("Coupons");

    Description(builder => builder
      .Accepts<CreateCouponRequest>("application/json")
      .Produces<CouponRecord>(201, "application/json")
      .ProducesProblemFE(400)
      .ProducesProblem(409));
  }

  public override async Task<Results<Created<CouponRecord>, ProblemHttpResult>> ExecuteAsync(
    CreateCouponRequest request, CancellationToken cancellationToken)
  {
    var couponId = CouponId.From(Guid.NewGuid());
    Coupon coupon;
    try
    {
      coupon = Coupon.Create(couponId, request.Code, request.DiscountPercentage, request.ExpirationDate);
    }
    catch (ArgumentException exception) when (
      (exception.GetType() == typeof(ArgumentException) || exception.GetType() == typeof(ArgumentNullException)) &&
      exception.ParamName is "code" or "discountPercentage" or "expirationDate")
    {
      switch (exception.ParamName)
      {
        case "code":
          ThrowError(r => r.Code, "Coupon code must not be blank and must contain between 3 and 30 characters.");
          break;
        case "discountPercentage":
          ThrowError(r => r.DiscountPercentage, "Discount percentage must be greater than zero and must not exceed 100.");
          break;
        case "expirationDate":
          ThrowError(r => r.ExpirationDate, "Expiration date must be in UTC and later than the creation time.");
          break;
      }

      throw;
    }

    Coupon persistedCoupon;
    try
    {
      persistedCoupon = await _repository.AddAsync(coupon, cancellationToken);
      await _repository.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception) when (IsDuplicateCouponCode(exception, coupon))
    {
      return TypedResults.Problem(
        title: "Coupon code conflict",
        detail: "Ya existe un cupón con ese código.",
        statusCode: StatusCodes.Status409Conflict);
    }

    var response = new CouponRecord(
      persistedCoupon.Id.Value,
      persistedCoupon.Code,
      persistedCoupon.DiscountPercentage,
      persistedCoupon.ExpirationDate,
      persistedCoupon.IsActive,
      persistedCoupon.CreatedAt);

    return TypedResults.Created($"/Coupons/{persistedCoupon.Id.Value}", response);
  }

  private static bool IsDuplicateCouponCode(DbUpdateException exception, Coupon coupon)
  {
    // SQLite exposes error codes but no structured constraint name or column list.
    // Match the complete provider message as well as the codes and EF entry.
    return exception.InnerException is SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 } sqliteException
      && exception.Entries.Count == 1
      && ReferenceEquals(exception.Entries[0].Entity, coupon)
      && exception.Entries[0].State == EntityState.Added
      && string.Equals(sqliteException.Message,
        "SQLite Error 19: 'UNIQUE constraint failed: Coupons.Code'.",
        StringComparison.Ordinal);
  }
}

public sealed class CreateCouponRequestValidator : Validator<CreateCouponRequest>
{
  public CreateCouponRequestValidator()
  {
    RuleFor(x => x.Code)
      .Cascade(CascadeMode.Stop)
      .NotEmpty()
      .WithMessage("Coupon code is required and must not be blank.")
      .Length(3, 30)
      .WithMessage("Coupon code must contain between 3 and 30 characters.");

    RuleFor(x => x.DiscountPercentage)
      .Cascade(CascadeMode.Stop)
      .GreaterThan(0m)
      .WithMessage("Discount percentage must be greater than zero.")
      .LessThanOrEqualTo(100m)
      .WithMessage("Discount percentage must not exceed 100.");

    RuleFor(x => x.ExpirationDate)
      .Cascade(CascadeMode.Stop)
      .NotEmpty()
      .WithMessage("Expiration date is required.")
      .Must(date => date.Kind == DateTimeKind.Utc)
      .WithMessage("Expiration date must be expressed in UTC.")
      .Must(date => date > DateTime.UtcNow)
      .WithMessage("Expiration date must be in the future.");
  }
}
