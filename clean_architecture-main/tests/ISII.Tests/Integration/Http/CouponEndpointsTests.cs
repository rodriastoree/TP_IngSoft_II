using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FastEndpoints;
using ISII.Tests.Support;
using ISII.Web.CouponFeatures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace ISII.Tests.Integration.Http;

public class CouponEndpointsTests : IAsyncLifetime
{
  private readonly SqliteTestDatabase _database = new();
  private CouponWebApplicationFactory? _factory;
  private HttpClient? _client;
  private bool _disposed;

  private CouponWebApplicationFactory Factory => _factory ?? throw new InvalidOperationException("The test host is not initialized.");
  private HttpClient Client => _client ?? throw new InvalidOperationException("The HTTP client is not initialized.");

  public async Task InitializeAsync()
  {
    try
    {
      await _database.InitializeAsync();
      _factory = new CouponWebApplicationFactory(_database);
      _client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
      await Factory.VerifyDatabaseAsync();
    }
    catch
    {
      await DisposeAsync();
      throw;
    }
  }

  public async Task DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;
    try
    {
      _client?.Dispose();
    }
    finally
    {
      try
      {
        if (_factory != null) await _factory.DisposeAsync();
      }
      finally
      {
        await _database.DisposeAsync();
      }
    }
  }

  [Fact]
  public async Task Post_ShouldCreateCouponWithLocationAndGetShouldReturnTheSameRecord()
  {
    const string code = " WeLcOmE10 ";
    const decimal percentage = 12.345678m;
    var expirationDate = DateTime.UtcNow.Date.AddDays(30).AddTicks(123);
    var beforeCreation = DateTime.UtcNow;

    using var postResponse = await PostCouponAsync(new { code, discountPercentage = percentage, expirationDate });

    var afterCreation = DateTime.UtcNow;
    Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
    var created = await ReadCouponAsync(postResponse);
    Assert.NotEqual(Guid.Empty, created.Id);
    Assert.Equal(code, created.Code);
    Assert.Equal(percentage, created.DiscountPercentage);
    Assert.Equal(expirationDate.Ticks, created.ExpirationDate.Ticks);
    Assert.Equal(DateTimeKind.Utc, created.ExpirationDate.Kind);
    Assert.Equal(DateTimeKind.Utc, created.CreatedAt.Kind);
    Assert.InRange(created.CreatedAt, beforeCreation, afterCreation);
    Assert.True(created.IsActive);
    Assert.Equal($"/Coupons/{created.Id}", postResponse.Headers.Location?.OriginalString);

    using var getResponse = await Client.GetAsync(postResponse.Headers.Location);
    Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    Assert.Equal(created, await ReadCouponAsync(getResponse));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("AB")]
  [InlineData("1234567890123456789012345678901")]
  public async Task Post_ShouldRejectInvalidCodeWithoutInsertingCoupon(string? code)
  {
    var request = CreateValidRequest();
    request["code"] = code;

    using var response = await PostCouponAsync(request);

    await AssertValidationErrorAsync(response, "Code");
    await AssertCouponCountAsync(0);
  }

  [Theory]
  [InlineData("0")]
  [InlineData("-1")]
  [InlineData("101")]
  public async Task Post_ShouldRejectInvalidPercentageWithoutInsertingCoupon(string percentage)
  {
    var request = CreateValidRequest();
    request["discountPercentage"] = decimal.Parse(percentage, CultureInfo.InvariantCulture);

    using var response = await PostCouponAsync(request);

    await AssertValidationErrorAsync(response, "DiscountPercentage");
    await AssertCouponCountAsync(0);
  }

  [Theory]
  [InlineData("missing")]
  [InlineData("past")]
  [InlineData("unspecified")]
  [InlineData("malformed")]
  public async Task Post_ShouldRejectInvalidExpirationWithoutInsertingCoupon(string scenario)
  {
    var request = CreateValidRequest();
    switch (scenario)
    {
      case "missing":
        request.Remove("expirationDate");
        break;
      case "past":
        request["expirationDate"] = DateTime.UtcNow.AddDays(-1);
        break;
      case "unspecified":
        request["expirationDate"] = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), DateTimeKind.Unspecified);
        break;
      case "malformed":
        request["expirationDate"] = "not-a-date";
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(scenario));
    }

    using var response = await PostCouponAsync(request);

    // Malformed JSON property values are rejected by binding before the validator runs.
    await AssertValidationErrorAsync(response, scenario == "malformed" ? null : "ExpirationDate");
    await AssertCouponCountAsync(0);
  }

  [Fact]
  public async Task Post_ShouldReturnSafeConflictForDuplicateCodeAndKeepOneCoupon()
  {
    var request = CreateValidRequest();
    using var firstResponse = await PostCouponAsync(request);
    Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
    var firstCoupon = await ReadCouponAsync(firstResponse);

    using var duplicateResponse = await PostCouponAsync(request);

    Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
    Assert.Equal("application/problem+json", duplicateResponse.Content.Headers.ContentType?.MediaType);
    var body = await duplicateResponse.Content.ReadAsStringAsync();
    using var document = JsonDocument.Parse(body);
    Assert.Equal(409, document.RootElement.GetProperty("status").GetInt32());
    Assert.Equal("Coupon code conflict", document.RootElement.GetProperty("title").GetString());
    Assert.Equal("Ya existe un cupón con ese código.", document.RootElement.GetProperty("detail").GetString());
    AssertSafePublicError(body);
    await AssertCouponCountAsync(1);
    Assert.Equal(firstCoupon, await GetCouponAsync(firstCoupon.Id));
  }

  [Fact]
  public async Task Post_ShouldPreserveCaseAndCreateTwoDistinctRecoverableCoupons()
  {
    var request = CreateValidRequest();
    request["code"] = "PROMO10";
    using var upperResponse = await PostCouponAsync(request);
    Assert.Equal(HttpStatusCode.Created, upperResponse.StatusCode);
    var upperCoupon = await ReadCouponAsync(upperResponse);
    request["code"] = "promo10";

    using var lowerResponse = await PostCouponAsync(request);

    Assert.Equal(HttpStatusCode.Created, lowerResponse.StatusCode);
    var lowerCoupon = await ReadCouponAsync(lowerResponse);
    Assert.NotEqual(upperCoupon.Id, lowerCoupon.Id);
    Assert.Equal("PROMO10", upperCoupon.Code);
    Assert.Equal("promo10", lowerCoupon.Code);
    Assert.Equal(upperCoupon, await GetCouponAsync(upperCoupon.Id));
    Assert.Equal(lowerCoupon, await GetCouponAsync(lowerCoupon.Id));
    await AssertCouponCountAsync(2);
  }

  [Fact]
  public async Task Get_ShouldReturnNotFoundForMissingCoupon()
  {
    var missingId = Guid.NewGuid();

    using var response = await Client.GetAsync($"/Coupons/{missingId}");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    await AssertCouponCountAsync(0);
  }

  [Fact]
  public async Task Get_ShouldRejectEmptyGuidWithProblemResponse()
  {
    using var response = await Client.GetAsync($"/Coupons/{Guid.Empty}");

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    var body = await response.Content.ReadAsStringAsync();
    using var document = JsonDocument.Parse(body);
    Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
    Assert.Equal("Invalid coupon ID", document.RootElement.GetProperty("title").GetString());
    Assert.Equal("Coupon ID must not be empty.", document.RootElement.GetProperty("detail").GetString());
    AssertSafePublicError(body);
    await AssertCouponCountAsync(0);
  }

  [Fact]
  public async Task Get_ShouldRejectMalformedGuidWithFastEndpointsErrorResponse()
  {
    using var response = await Client.GetAsync("/Coupons/not-a-guid");

    await AssertValidationErrorAsync(response, "CouponId");
    await AssertCouponCountAsync(0);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Get_ShouldReturnExpiredOrInactiveCouponWithoutFiltering(bool expired)
  {
    var request = CreateValidRequest();
    request["code"] = expired ? "EXPIRED" : "INACTIVE";
    using var createResponse = await PostCouponAsync(request);
    Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
    var created = await ReadCouponAsync(createResponse);
    var now = DateTime.UtcNow;
    var createdAt = expired ? now.AddDays(-2) : created.CreatedAt;
    var expirationDate = expired ? now.AddDays(-1) : created.ExpirationDate;
    var isActive = expired;
    await Factory.VerifyDatabaseAsync();
    await using (var context = _database.CreateContext())
    {
      var affectedRows = await context.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE "Coupons"
        SET "CreatedAt" = {createdAt.Ticks}, "ExpirationDate" = {expirationDate.Ticks}, "IsActive" = {isActive}
        WHERE "Id" = {created.Id};
        """);
      Assert.Equal(1, affectedRows);
    }

    var recovered = await GetCouponAsync(created.Id);

    Assert.Equal(created with { CreatedAt = createdAt, ExpirationDate = expirationDate, IsActive = isActive }, recovered);
    Assert.True(recovered.ExpirationDate > recovered.CreatedAt);
    await AssertCouponCountAsync(1);
  }

  [Fact]
  public async Task Post_ShouldReturnGeneric500WithoutInsertingCouponAndKeepFailureIsolated()
  {
    var request = CreateValidRequest();
    Assert.Equal(0, Factory.SaveFailureTriggerCount);
    await Factory.EnableSaveFailureAsync();

    using (var response = await PostCouponAsync(request))
    {
      Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
      var body = await response.Content.ReadAsStringAsync();
      var error = JsonSerializer.Deserialize<InternalErrorResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
      Assert.NotNull(error);
      Assert.Equal(500, error.Code);
      Assert.Equal("An unexpected error has occurred.", error.Reason);
      AssertSafePublicError(body);
    }

    Assert.Equal(1, Factory.SaveFailureTriggerCount);
    await AssertCouponCountAsync(0);

    // The artificial failure is consumed once and belongs only to this factory instance.
    request["code"] = "AFTERFAILURE";
    using var recoveryResponse = await PostCouponAsync(request);
    Assert.Equal(HttpStatusCode.Created, recoveryResponse.StatusCode);
    var recovered = await ReadCouponAsync(recoveryResponse);
    Assert.Equal(recovered, await GetCouponAsync(recovered.Id));
    Assert.Equal(1, Factory.SaveFailureTriggerCount);
    await AssertCouponCountAsync(1);
  }

  private static Dictionary<string, object?> CreateValidRequest() => new()
  {
    ["code"] = "WELCOME10",
    ["discountPercentage"] = 12.345678m,
    ["expirationDate"] = DateTime.UtcNow.Date.AddDays(30).AddTicks(123)
  };

  private async Task<CouponRecord> GetCouponAsync(Guid id)
  {
    using var response = await Client.GetAsync($"/Coupons/{id}");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    return await ReadCouponAsync(response);
  }

  private async Task AssertCouponCountAsync(int expectedCount)
  {
    await using var context = _database.CreateContext();
    Assert.Equal(expectedCount, await context.Coupons.CountAsync());
  }

  private async Task AssertValidationErrorAsync(HttpResponseMessage response, string? expectedField)
  {
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    var error = JsonSerializer.Deserialize<ErrorResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    Assert.NotNull(error);
    Assert.Equal(400, error.StatusCode);
    Assert.NotEmpty(error.Errors);
    if (expectedField != null)
    {
      Assert.Contains(error.Errors.Keys, field => string.Equals(field, expectedField, StringComparison.OrdinalIgnoreCase));
    }
    Assert.All(error.Errors.Values, messages =>
    {
      Assert.NotEmpty(messages);
      Assert.All(messages, message => Assert.False(string.IsNullOrWhiteSpace(message)));
    });
    AssertSafePublicError(body);
  }

  private void AssertSafePublicError(string body)
  {
    using var document = JsonDocument.Parse(body);
    var publicText = body + "\n" + string.Join("\n", EnumerateJsonStrings(document.RootElement));
    foreach (var internalDetail in new[]
      {
        CouponWebApplicationFactory.SimulatedFailureMessage,
        "SQLite", "DbUpdateException", "InvalidOperationException", "stackTrace", ".cs:line",
        "SELECT ", "INSERT ", "UPDATE ", "DataSource", "Data Source", "connectionString",
        _database.DatabasePath, Path.GetDirectoryName(_database.DatabasePath)!, AppContext.BaseDirectory
      })
    {
      Assert.DoesNotContain(internalDetail, publicText, StringComparison.OrdinalIgnoreCase);
    }
  }

  private static IEnumerable<string> EnumerateJsonStrings(JsonElement element)
  {
    if (element.ValueKind == JsonValueKind.String)
    {
      yield return element.GetString()!;
    }
    else if (element.ValueKind == JsonValueKind.Object)
    {
      foreach (var property in element.EnumerateObject())
      {
        yield return property.Name;
        foreach (var value in EnumerateJsonStrings(property.Value)) yield return value;
      }
    }
    else if (element.ValueKind == JsonValueKind.Array)
    {
      foreach (var item in element.EnumerateArray())
        foreach (var value in EnumerateJsonStrings(item)) yield return value;
    }
  }

  private async Task<HttpResponseMessage> PostCouponAsync<T>(T request)
  {
    await Factory.VerifyDatabaseAsync();
    return await Client.PostAsJsonAsync("/Coupons", request);
  }

  private static async Task<CouponRecord> ReadCouponAsync(HttpResponseMessage response)
  {
    Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    var json = await response.Content.ReadAsStringAsync();
    using var document = JsonDocument.Parse(json);
    Assert.Equal(6, document.RootElement.EnumerateObject().Count());
    Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("id").ValueKind);
    var record = JsonSerializer.Deserialize<CouponRecord>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    Assert.NotNull(record);
    Assert.Equal(DateTimeKind.Utc, record.ExpirationDate.Kind);
    Assert.Equal(DateTimeKind.Utc, record.CreatedAt.Kind);
    return record;
  }
}
