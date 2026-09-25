using ISII.Web.Domain.ProductAggregate;

namespace ISII.Web.ProductFeatures;
public record ProductDto(ProductId Id, string Name, decimal UnitPrice);
