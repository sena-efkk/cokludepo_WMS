namespace Wms.Modules.Facility.Domain;

public sealed class Location : IHasTimestamps
{
    private Location()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private Location(
        Guid warehouseId,
        Guid? parentLocationId,
        string code,
        string name,
        LocationType type,
        bool allowsPicking,
        bool allowsPutaway,
        bool allowsReplenishment,
        bool holdsInventory)
    {
        Id = Guid.NewGuid();
        WarehouseId = warehouseId;
        ParentLocationId = parentLocationId;
        Code = code;
        Name = name;
        Type = type;
        AllowsPicking = allowsPicking;
        AllowsPutaway = allowsPutaway;
        AllowsReplenishment = allowsReplenishment;
        HoldsInventory = holdsInventory;
        IsActive = true;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid? ParentLocationId { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public LocationType Type { get; private set; }

    public bool AllowsPicking { get; private set; }

    public bool AllowsPutaway { get; private set; }

    public bool AllowsReplenishment { get; private set; }

    public bool HoldsInventory { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public static Location Create(
        Guid warehouseId,
        Guid? parentLocationId,
        string code,
        string name,
        LocationType type,
        bool allowsPicking = false,
        bool allowsPutaway = false,
        bool allowsReplenishment = false,
        bool holdsInventory = false)
    {
        if (warehouseId == Guid.Empty)
        {
            throw new ArgumentException("Location bir Warehouse'a bağlı olmalıdır.", nameof(warehouseId));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Location kodu boş olamaz.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Location adı boş olamaz.", nameof(name));
        }

        return new Location(
            warehouseId,
            parentLocationId,
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            type,
            allowsPicking,
            allowsPutaway,
            allowsReplenishment,
            holdsInventory);
    }

    public void SetParent(Guid? parentLocationId)
    {
        if (parentLocationId == Id)
        {
            throw new ArgumentException("Location kendi parent'ı olamaz.", nameof(parentLocationId));
        }

        ParentLocationId = parentLocationId;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
