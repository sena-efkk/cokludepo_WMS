namespace Wms.Modules.MasterData.Domain;

public sealed class SkuBarcode
{
    private SkuBarcode()
    {
        Value = string.Empty;
    }

    private SkuBarcode(Guid skuId, string value, BarcodeType type)
    {
        Id = Guid.NewGuid();
        SkuId = skuId;
        Value = value;
        Type = type;
    }

    public Guid Id { get; private set; }

    public Guid SkuId { get; private set; }

    public string Value { get; private set; }

    public BarcodeType Type { get; private set; }

    public static SkuBarcode Create(Guid skuId, string value, BarcodeType type)
    {
        if (skuId == Guid.Empty)
        {
            throw new ArgumentException("Barcode bir SKU'ya bağlı olmalıdır.", nameof(skuId));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Barcode boş olamaz.", nameof(value));
        }

        return new SkuBarcode(skuId, value.Trim(), type);
    }
}
