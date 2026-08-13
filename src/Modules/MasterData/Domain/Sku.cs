namespace Wms.Modules.MasterData.Domain;

public sealed class Sku : IHasTimestamps
{
    private readonly List<SkuBarcode> _barcodes = [];

    private Sku()
    {
        Code = string.Empty;
    }

    private Sku(
        Guid productId,
        string code,
        Guid uomId,
        string? name,
        decimal? weightKg,
        decimal? lengthCm,
        decimal? widthCm,
        decimal? heightCm)
    {
        Id = Guid.NewGuid();
        ProductId = productId;
        Code = code;
        UomId = uomId;
        Name = name;
        WeightKg = weightKg;
        LengthCm = lengthCm;
        WidthCm = widthCm;
        HeightCm = heightCm;
        IsActive = true;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid ProductId { get; private set; }

    public string Code { get; private set; }

    public string? Name { get; private set; }

    public Guid UomId { get; private set; }

    public decimal? WeightKg { get; private set; }

    public decimal? LengthCm { get; private set; }

    public decimal? WidthCm { get; private set; }

    public decimal? HeightCm { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public Product? Product { get; private set; }

    public Uom? Uom { get; private set; }

    public IReadOnlyCollection<SkuBarcode> Barcodes => _barcodes;

    public static Sku Create(
        Guid productId,
        string code,
        Guid uomId,
        string? name = null,
        decimal? weightKg = null,
        decimal? lengthCm = null,
        decimal? widthCm = null,
        decimal? heightCm = null)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("SKU bir Product'a bağlı olmalıdır.", nameof(productId));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("SKU kodu boş olamaz.", nameof(code));
        }

        if (uomId == Guid.Empty)
        {
            throw new ArgumentException("SKU bir UOM'a bağlı olmalıdır.", nameof(uomId));
        }

        ValidateMeasurement(weightKg, nameof(weightKg));
        ValidateMeasurement(lengthCm, nameof(lengthCm));
        ValidateMeasurement(widthCm, nameof(widthCm));
        ValidateMeasurement(heightCm, nameof(heightCm));

        return new Sku(productId, code.Trim(), uomId, name?.Trim(), weightKg, lengthCm, widthCm, heightCm);
    }

    public void AddBarcode(string value, BarcodeType type)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Barcode boş olamaz.", nameof(value));
        }

        var normalized = value.Trim();
        if (_barcodes.Any(b => string.Equals(b.Value, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Bu SKU'da aynı barcode zaten var: {normalized}", nameof(value));
        }

        _barcodes.Add(SkuBarcode.Create(Id, normalized, type));
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    private static void ValidateMeasurement(decimal? value, string parameterName)
    {
        if (value is < 0)
        {
            throw new ArgumentException("Ölçü negatif olamaz.", parameterName);
        }
    }
}
