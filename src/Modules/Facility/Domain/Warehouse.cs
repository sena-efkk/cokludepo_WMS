namespace Wms.Modules.Facility.Domain;

public sealed class Warehouse : IHasTimestamps
{
    private Warehouse()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private Warehouse(
        string code,
        string name,
        string? addressLine,
        string? city,
        string? countryCode,
        decimal? latitude,
        decimal? longitude)
    {
        Id = Guid.NewGuid();
        Code = code;
        Name = name;
        AddressLine = addressLine;
        City = city;
        CountryCode = countryCode;
        Latitude = latitude;
        Longitude = longitude;
        IsActive = true;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? AddressLine { get; private set; }

    public string? City { get; private set; }

    public string? CountryCode { get; private set; }

    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public static Warehouse Create(
        string code,
        string name,
        string? addressLine = null,
        string? city = null,
        string? countryCode = null,
        decimal? latitude = null,
        decimal? longitude = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Warehouse kodu boş olamaz.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Warehouse adı boş olamaz.", nameof(name));
        }

        if (latitude is < -90 or > 90)
        {
            throw new ArgumentException("Latitude -90..90 aralığında olmalıdır.", nameof(latitude));
        }

        if (longitude is < -180 or > 180)
        {
            throw new ArgumentException("Longitude -180..180 aralığında olmalıdır.", nameof(longitude));
        }

        return new Warehouse(
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            addressLine,
            city,
            countryCode,
            latitude,
            longitude);
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
