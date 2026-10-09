using ISII.Web.Domain.CartAggregate;
using ISII.Web.Domain.GuestUserAggregate;
using ISII.Web.Domain.OrderAggregate;
using ISII.Web.Domain.ProductAggregate;
using Vogen;

namespace ISII.Web.Infrastructure.Data.Config;

[EfCoreConverter<ProductId>]
[EfCoreConverter<CartId>]
[EfCoreConverter<CartItemId>]
[EfCoreConverter<GuestUserId>]
[EfCoreConverter<OrderId>]
[EfCoreConverter<OrderItemId>]
[EfCoreConverter<Quantity>]
[EfCoreConverter<Price>]
internal partial class VogenEfCoreConverters;
