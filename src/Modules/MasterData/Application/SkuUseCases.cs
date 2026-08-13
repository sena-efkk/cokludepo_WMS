using Wms.Modules.MasterData.Domain;

namespace Wms.Modules.MasterData.Application;

public sealed record CreateSkuCommand(
    Guid ProductId,
    string? Code,
    string? Name,
    string? Barcode,
    string? UomCode,
    decimal? WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm);

public sealed class CreateSku(IMasterDataStore store)
{
    public const string DefaultUomCode = "EA";

    public async Task<Sku> Handle(CreateSkuCommand command, CancellationToken cancellationToken)
    {
        var product = await store.GetProductAsync(command.ProductId, cancellationToken)
            ?? throw new ProductNotFoundException(command.ProductId);

        var uomCode = string.IsNullOrWhiteSpace(command.UomCode) ? DefaultUomCode : command.UomCode.Trim();
        var uom = await store.GetUomByCodeAsync(uomCode, cancellationToken)
            ?? throw new UomNotFoundException(uomCode);

        string code;
        if (string.IsNullOrWhiteSpace(command.Code))
        {
            var sequence = await store.NextSkuSequenceAsync(cancellationToken);
            code = SkuCodeGenerator.Format(sequence);
        }
        else
        {
            code = command.Code.Trim();
            if (await store.GetSkuByCodeAsync(code, cancellationToken) is not null)
            {
                throw new DuplicateSkuException($"SKU kodu zaten kullanımda: {code}");
            }
        }

        if (!string.IsNullOrWhiteSpace(command.Barcode))
        {
            var barcode = command.Barcode.Trim();
            if (await store.GetSkuByBarcodeAsync(barcode, cancellationToken) is not null)
            {
                throw new DuplicateSkuException($"Barcode zaten kullanımda: {barcode}");
            }
        }

        var sku = Sku.Create(
            product.Id,
            code,
            uom.Id,
            command.Name,
            command.WeightKg,
            command.LengthCm,
            command.WidthCm,
            command.HeightCm);

        if (!string.IsNullOrWhiteSpace(command.Barcode))
        {
            sku.AddBarcode(command.Barcode.Trim(), BarcodeType.Ean);
        }

        await store.AddSkuAsync(sku, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return sku;
    }
}

public sealed class GetSku(IMasterDataStore store)
{
    public async Task<Sku?> Handle(Guid skuId, CancellationToken cancellationToken)
    {
        return await store.GetSkuAsync(skuId, cancellationToken);
    }
}

public sealed class ListSkus(IMasterDataStore store)
{
    public async Task<IReadOnlyList<Sku>> Handle(Guid? productId, string? search, bool includeInactive, CancellationToken cancellationToken)
    {
        return await store.ListSkusAsync(productId, search, includeInactive, cancellationToken);
    }
}

public sealed class DeactivateSku(IMasterDataStore store)
{
    public async Task Handle(Guid skuId, CancellationToken cancellationToken)
    {
        var sku = await store.GetSkuAsync(skuId, cancellationToken)
            ?? throw new SkuNotFoundException(skuId);

        if (sku.IsActive)
        {
            sku.Deactivate();
            await store.SaveChangesAsync(cancellationToken);
        }
    }
}
