using Microsoft.EntityFrameworkCore;
using Wms.Modules.Facility.Domain;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.MasterData.Domain;
using Wms.Modules.MasterData.Infrastructure.Persistence;

namespace Wms.InventoryTests;

internal static class Db
{
    public static InventoryDbContext CreateInventoryContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(TestConnection.ResolveOrFail(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "inventory"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new InventoryDbContext(options);
    }

    public static MasterDataDbContext CreateMasterDataContext()
    {
        var options = new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseNpgsql(TestConnection.ResolveOrFail(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "master_data"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new MasterDataDbContext(options);
    }

    public static FacilityDbContext CreateFacilityContext()
    {
        var options = new DbContextOptionsBuilder<FacilityDbContext>()
            .UseNpgsql(TestConnection.ResolveOrFail(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "facility"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new FacilityDbContext(options);
    }

    public static async Task<Guid> CreateSkuAsync()
    {
        await using var db = CreateMasterDataContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var uom = await db.Uoms.FirstAsync(u => u.Code == "EA");
        var product = Product.Create($"Test Product {suffix}");
        var sku = Sku.Create(product.Id, $"INV-{suffix}", uom.Id);
        db.Add(product);
        db.Add(sku);
        await db.SaveChangesAsync();
        return sku.Id;
    }

    public static async Task<(Guid SkuId, string Barcode)> CreateSkuWithBarcodeAsync()
    {
        await using var db = CreateMasterDataContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var barcode = $"BC-{suffix}";
        var uom = await db.Uoms.FirstAsync(u => u.Code == "EA");
        var product = Product.Create($"Test Product {suffix}");
        var sku = Sku.Create(product.Id, $"INV-{suffix}", uom.Id);
        sku.AddBarcode(barcode, BarcodeType.Ean);
        db.Add(product);
        db.Add(sku);
        await db.SaveChangesAsync();
        return (sku.Id, barcode);
    }

    public static async Task<(Guid WarehouseId, Guid LocationId)> CreateWarehouseWithStorageLocationAsync(bool allowsPicking = false)
    {
        var (warehouseId, locationId, _) = await CreateWarehouseWithStorageLocationWithCodeAsync(allowsPicking);
        return (warehouseId, locationId);
    }

    public static async Task<(Guid WarehouseId, Guid LocationId, string LocationCode)> CreateWarehouseWithStorageLocationWithCodeAsync(bool allowsPicking = false)
    {
        await using var db = CreateFacilityContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var warehouse = Warehouse.Create($"INV-W-{suffix}", $"Inv Test Depo {suffix}");
        var location = Location.Create(warehouse.Id, null, $"STORE-{suffix}", "Test Stok Lokasyonu", LocationType.Storage, allowsPicking: allowsPicking, holdsInventory: true);
        db.Add(warehouse);
        db.Add(location);
        await db.SaveChangesAsync();
        return (warehouse.Id, location.Id, location.Code);
    }

    public static async Task<(Guid LocationId, string LocationCode)> CreateLocationAsync(
        Guid warehouseId,
        LocationType type,
        bool holdsInventory)
    {
        await using var db = CreateFacilityContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var location = Location.Create(warehouseId, null, $"{type.ToString().ToUpperInvariant()}-{suffix}", $"Test {type}", type, allowsPicking: false, holdsInventory: holdsInventory);
        db.Add(location);
        await db.SaveChangesAsync();
        return (location.Id, location.Code);
    }
}
