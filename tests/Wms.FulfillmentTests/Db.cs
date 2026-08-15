using Microsoft.EntityFrameworkCore;
using Wms.Modules.Facility.Domain;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Fulfillment.Infrastructure.Persistence;
using Wms.Modules.Inbound.Infrastructure.Persistence;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.MasterData.Domain;
using Wms.Modules.MasterData.Infrastructure.Persistence;
using Wms.Modules.Outbound.Infrastructure.Persistence;
using Wms.Modules.Transfers.Infrastructure.Persistence;

namespace Wms.FulfillmentTests;

internal static class Db
{
    public static FulfillmentDbContext CreateFulfillmentContext()
    {
        var options = new DbContextOptionsBuilder<FulfillmentDbContext>()
            .UseNpgsql(TestConnection.ResolveOrFail(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "fulfillment"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new FulfillmentDbContext(options);
    }

    public static OutboundDbContext CreateOutboundContext()
    {
        var options = new DbContextOptionsBuilder<OutboundDbContext>()
            .UseNpgsql(TestConnection.ResolveOrFail(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "outbound"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new OutboundDbContext(options);
    }

    public static InboundDbContext CreateInboundContext()
    {
        var options = new DbContextOptionsBuilder<InboundDbContext>()
            .UseNpgsql(TestConnection.ResolveOrFail(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "inbound"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new InboundDbContext(options);
    }

    public static TransfersDbContext CreateTransfersContext()
    {
        var options = new DbContextOptionsBuilder<TransfersDbContext>()
            .UseNpgsql(TestConnection.ResolveOrFail(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "transfers"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new TransfersDbContext(options);
    }

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

    public static async Task<(Guid SkuId, string Barcode)> CreateSkuWithBarcodeAsync(string? codePrefix = "NET")
    {
        await using var db = CreateMasterDataContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var barcode = $"BC-{suffix}";
        var uom = await db.Uoms.FirstAsync(u => u.Code == "EA");
        var product = Product.Create($"Net Product {suffix}");
        var sku = Sku.Create(product.Id, $"{codePrefix}-{suffix}", uom.Id);
        sku.AddBarcode(barcode, BarcodeType.Ean);
        db.Add(product);
        db.Add(sku);
        await db.SaveChangesAsync();
        return (sku.Id, barcode);
    }

    public static async Task<Guid> CreateSkuAsync()
    {
        await using var db = CreateMasterDataContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var uom = await db.Uoms.FirstAsync(u => u.Code == "EA");
        var product = Product.Create($"Net Product {suffix}");
        var sku = Sku.Create(product.Id, $"NET-{suffix}", uom.Id);
        db.Add(product);
        db.Add(sku);
        await db.SaveChangesAsync();
        return sku.Id;
    }

    public static async Task<Guid> CreateWarehouseAsync()
    {
        await using var db = CreateFacilityContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var warehouse = Warehouse.Create($"NET-W-{suffix}", $"Network Test Depo {suffix}");
        db.Add(warehouse);
        await db.SaveChangesAsync();
        return warehouse.Id;
    }

    public static async Task<(Guid LocationId, string Code)> CreateStorageLocationAsync(Guid warehouseId)
    {
        await using var db = CreateFacilityContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var location = Location.Create(
            warehouseId,
            null,
            $"LOC-{suffix}",
            "Network Test Lokasyon",
            LocationType.Storage,
            allowsPicking: true,
            holdsInventory: true);
        db.Add(location);
        await db.SaveChangesAsync();
        return (location.Id, location.Code);
    }

    public static async Task<(Guid LocationId, string Code)> CreateLocationAsync(Guid warehouseId, LocationType type)
    {
        await using var db = CreateFacilityContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var location = Location.Create(
            warehouseId,
            null,
            $"{type.ToString().ToUpperInvariant()}-{suffix}",
            $"Test {type}",
            type,
            allowsPicking: type == LocationType.Storage,
            holdsInventory: true);
        db.Add(location);
        await db.SaveChangesAsync();
        return (location.Id, location.Code);
    }
}
