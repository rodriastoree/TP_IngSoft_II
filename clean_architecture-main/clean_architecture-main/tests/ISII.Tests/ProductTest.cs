using ISII.Web.Domain.ProductAggregate;

namespace ISII.Tests;

public class ProductTest
{
    [Fact]
    public void Constructor_ShouldAssignProvidedValues()
    {
        var id = ProductId.New;
        const string name = "Laptop";
        const decimal unitPrice = 1299.99m;

        var product = new Product(id, name, unitPrice);

        Assert.Equal(id, product.Id);
        Assert.Equal(name, product.Name);
        Assert.Equal(unitPrice, product.UnitPrice);
    }

    [Fact]
    public void Create_ShouldReturnProductWithProvidedValues()
    {
        var id = ProductId.New;
        const string name = "Keyboard";
        const decimal unitPrice = 89.95m;

        var product = Product.Create(id, name, unitPrice);

        Assert.NotNull(product);
        Assert.Equal(id, product.Id);
        Assert.Equal(name, product.Name);
        Assert.Equal(unitPrice, product.UnitPrice);
    }

    [Fact]
    public void UpdateName_ShouldChangeNameAndReturnSameInstance()
    {
        var product = new Product(ProductId.New, "Old name", 10m);

        var result = product.UpdateName("New name");

        Assert.Same(product, result);
        Assert.Equal("New name", product.Name);
    }

    [Fact]
    public void UpdateName_ShouldNotChangeIdOrUnitPrice()
    {
        var id = ProductId.New;
        const decimal unitPrice = 75.50m;
        var product = new Product(id, "Original", unitPrice);

        product.UpdateName("Updated");

        Assert.Equal(id, product.Id);
        Assert.Equal(unitPrice, product.UnitPrice);
    }

    [Fact]
    public void UpdatePrice_ShouldChangePriceAndReturnSameInstance()
    {
        var product = new Product(ProductId.New, "Monitor", 199.99m);

        var result = product.UpdatePrice(249.99m);

        Assert.Same(product, result);
        Assert.Equal(249.99m, product.UnitPrice);
    }

    [Fact]
    public void UpdatePrice_ShouldNotChangeIdOrName()
    {
        var id = ProductId.New;
        const string name = "Original";
        var product = new Product(id, name, 75.50m);

        product.UpdatePrice(120.75m);

        Assert.Equal(id, product.Id);
        Assert.Equal(name, product.Name);
    }

    [Fact]
    public void Constructor_ShouldSupportZeroUnitPrice()
    {
        var product = new Product(ProductId.New, "Free product", 0m);

        Assert.Equal(0m, product.UnitPrice);
    }
}