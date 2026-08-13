namespace Wms.Modules.Inventory.Domain;

public sealed class InventoryBalance : IHasTimestamps
{
    private InventoryBalance()
    {
    }

    private InventoryBalance(Guid skuId, Guid warehouseId, Guid locationId, InventoryStatus status, int quantity)
    {
        Id = Guid.NewGuid();
        SkuId = skuId;
        WarehouseId = warehouseId;
        LocationId = locationId;
        Status = status;
        Quantity = quantity;
        Allocated = 0;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid SkuId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid LocationId { get; private set; }

    public InventoryStatus Status { get; private set; }

    public int Quantity { get; private set; }

    public int Allocated { get; private set; }

    public uint Version { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public int Available => Status == InventoryStatus.Available ? Quantity - Allocated : 0;

    public int UnallocatedQuantity => Status == InventoryStatus.Available ? Quantity - Allocated : Quantity;

    public static InventoryBalance Create(Guid skuId, Guid warehouseId, Guid locationId, InventoryStatus status, int quantity)
    {
        if (skuId == Guid.Empty)
        {
            throw new ArgumentException("Balance bir SKU'ya bağlı olmalıdır.", nameof(skuId));
        }

        if (warehouseId == Guid.Empty)
        {
            throw new ArgumentException("Balance bir Warehouse'a bağlı olmalıdır.", nameof(warehouseId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("Balance bir Location'a bağlı olmalıdır.", nameof(locationId));
        }

        if (quantity < 0)
        {
            throw new ArgumentException("Quantity negatif olamaz.", nameof(quantity));
        }

        return new InventoryBalance(skuId, warehouseId, locationId, status, quantity);
    }

    internal void IncreaseQuantity(int delta)
    {
        if (delta < 0)
        {
            throw new ArgumentException("Delta negatif olamaz.", nameof(delta));
        }

        Quantity += delta;
    }

    internal void DecreaseQuantityUnallocated(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Düşürülecek miktar pozitif olmalıdır.", nameof(quantity));
        }

        if (UnallocatedQuantity < quantity)
        {
            throw new InvalidOperationException(
                $"Yalnızca serbest (unallocated) stok taşınabilir: {quantity} istendi, {UnallocatedQuantity} serbest.");
        }

        Quantity -= quantity;
    }

    internal void ApplyAdjustment(int quantityDelta)
    {
        if (quantityDelta == 0)
        {
            throw new ArgumentException("Adjustment deltası sıfır olamaz.", nameof(quantityDelta));
        }

        var newQuantity = Quantity + quantityDelta;
        if (newQuantity < 0)
        {
            throw new InvalidOperationException($"Adjustment negatif stok üretemez: {Quantity} + {quantityDelta} = {newQuantity}.");
        }

        if (newQuantity < Allocated)
        {
            throw new InvalidOperationException(
                $"Adjustment allocated invariant'ını bozamaz: yeni miktar {newQuantity} < allocated {Allocated}.");
        }

        Quantity = newQuantity;
    }

    internal void AddAllocated(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Ayrılacak miktar pozitif olmalıdır.", nameof(quantity));
        }

        if (Status != InventoryStatus.Available)
        {
            throw new InvalidOperationException("Yalnızca AVAILABLE statüsündeki stok allocate edilebilir.");
        }

        if (Allocated + quantity > Quantity)
        {
            throw new InvalidOperationException("Allocated, Quantity'yi aşamaz.");
        }

        Allocated += quantity;
    }

    internal void SubtractAllocated(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Geri alınacak miktar pozitif olmalıdır.", nameof(quantity));
        }

        if (Allocated - quantity < 0)
        {
            throw new InvalidOperationException("Allocated negatif olamaz.");
        }

        Allocated -= quantity;
    }

    internal void Consume(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Tüketilecek miktar pozitif olmalıdır.", nameof(quantity));
        }

        if (Allocated < quantity || Quantity < quantity)
        {
            throw new InvalidOperationException("Tüketilecek miktar mevcut quantity/allocated değerlerini aşamaz.");
        }

        Quantity -= quantity;
        Allocated -= quantity;
    }
}
