using Wms.Modules.MasterData.Domain;

namespace Wms.Modules.MasterData.Application;

public sealed record CreateProductCommand(string Name, string? Description, Guid? BrandId, Guid? CategoryId);

public sealed class CreateProduct(IMasterDataStore store)
{
    public async Task<Product> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var product = Product.Create(command.Name, command.Description, command.BrandId, command.CategoryId);
        await store.AddProductAsync(product, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return product;
    }
}

public sealed class GetProduct(IMasterDataStore store)
{
    public async Task<Product?> Handle(Guid productId, CancellationToken cancellationToken)
    {
        return await store.GetProductAsync(productId, cancellationToken);
    }
}

public sealed class ListProducts(IMasterDataStore store)
{
    public async Task<IReadOnlyList<Product>> Handle(string? search, bool includeInactive, CancellationToken cancellationToken)
    {
        return await store.ListProductsAsync(search, includeInactive, cancellationToken);
    }
}
