namespace Wms.Modules.MasterData.Application;

public class MasterDataNotFoundException : Exception
{
    public MasterDataNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class ProductNotFoundException : MasterDataNotFoundException
{
    public ProductNotFoundException(Guid productId)
        : base($"Product bulunamadı: {productId}")
    {
    }
}

public sealed class SkuNotFoundException : MasterDataNotFoundException
{
    public SkuNotFoundException(Guid skuId)
        : base($"SKU bulunamadı: {skuId}")
    {
    }
}

public sealed class UomNotFoundException : MasterDataNotFoundException
{
    public UomNotFoundException(string uomCode)
        : base($"UOM bulunamadı: {uomCode}")
    {
    }
}

public sealed class DuplicateSkuException : Exception
{
    public DuplicateSkuException(string message)
        : base(message)
    {
    }
}
