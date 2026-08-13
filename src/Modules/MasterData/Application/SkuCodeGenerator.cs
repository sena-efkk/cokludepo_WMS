namespace Wms.Modules.MasterData.Application;

public static class SkuCodeGenerator
{
    public const string Prefix = "SKU-";

    public static string Format(long sequence)
    {
        return $"{Prefix}{sequence:D6}";
    }
}
