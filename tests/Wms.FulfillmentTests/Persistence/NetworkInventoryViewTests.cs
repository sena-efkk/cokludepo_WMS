using Microsoft.EntityFrameworkCore;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Fulfillment.Application;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Application.Accuracy;
using Wms.Modules.Inventory.Application.Accuracy.CycleCounting;
using Wms.Modules.Inventory.Application.Accuracy.Scanning;
using Wms.Modules.Inventory.Contracts;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Infrastructure;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.MasterData.Infrastructure.Persistence;
using Xunit;

namespace Wms.FulfillmentTests.Persistence;

public sealed class NetworkInventoryViewTests
{
    private static async Task<World> CreateWorldAsync()
    {
        var (sku, barcode) = await Db.CreateSkuWithBarcodeAsync();
        var warehouse = await Db.CreateWarehouseAsync();
        var (locA, _) = await Db.CreateStorageLocationAsync(warehouse);
        var (locB, _) = await Db.CreateStorageLocationAsync(warehouse);
        return new World(sku, barcode, warehouse, locA, locB);
    }

    private static async Task<Bundle> CreateBundleAsync()
    {
        var inventoryDb = Db.CreateInventoryContext();
        var facilityDb = Db.CreateFacilityContext();
        var masterDb = Db.CreateMasterDataContext();

        var inventoryStore = new InventoryStore(inventoryDb);
        var facilityContract = new FacilityQueryContract(facilityDb);
        var masterContract = new MasterDataQueryContract(masterDb);

        var adapter = new InventoryContractAdapter(
            inventoryStore,
            new Reserve(inventoryStore, masterContract, facilityContract),
            new ReserveOrder(inventoryStore, masterContract, facilityContract),
            new GetReservationById(inventoryStore),
            new ReleaseReservation(inventoryStore),
            new ConsumeReservation(inventoryStore),
            new GetWarehouseSkuSummary(inventoryStore),
            new ReportPickNotFound(inventoryStore, masterContract, facilityContract),
            new ReceiveInventory(inventoryStore, masterContract, facilityContract),
            new ExecuteScannedRelocation(
                inventoryStore,
                masterContract,
                facilityContract,
                new RelocateStock(inventoryStore, masterContract, facilityContract)),
            new ListRiskAssessments(inventoryStore, facilityContract, new InventoryRiskAnalyzer(new RiskPolicyOptions())));

        return new Bundle(
            inventoryStore,
            adapter,
            new NetworkInventoryView(masterContract, adapter, facilityContract, new NoTransfersContract()),
            facilityDb,
            inventoryDb,
            masterDb);
    }

    private sealed class NoTransfersContract : Wms.Modules.Transfers.Contracts.ITransferContract
    {
        public Task<int> GetOpenInTransitTotalAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<int> GetOpenInTransitBySkuAsync(Guid skuId, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private static async Task OpenStockAsync(World world, Guid skuId, Guid locationId, InventoryStatus status, int quantity)
    {
        await using var inventoryDb = Db.CreateInventoryContext();
        await using var facilityDb = Db.CreateFacilityContext();
        await using var masterDb = Db.CreateMasterDataContext();
        var store = new InventoryStore(inventoryDb);
        var opening = new RecordOpeningBalance(
            store,
            new MasterDataQueryContract(masterDb),
            new FacilityQueryContract(facilityDb));
        await opening.Handle(
            new RecordOpeningBalanceCommand(Guid.NewGuid(), skuId, world.Warehouse, locationId, status, quantity),
            CancellationToken.None);
    }

    // 1 — Tek warehouse physical stock doğru hesaplanır.
    [Fact]
    public async Task Single_warehouse_physical_stock_is_correct()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.Sku, world.LocA, InventoryStatus.Available, 10);
        await OpenStockAsync(world, world.Sku, world.LocA, InventoryStatus.Hold, 3);
        await OpenStockAsync(world, world.Sku, world.LocB, InventoryStatus.Damaged, 2);
        await using var bundle = await CreateBundleAsync();

        var view = await bundle.View.GetSkuAsync(world.Sku, CancellationToken.None);

        Assert.NotNull(view);
        Assert.Equal(15, view!.NetworkPhysicalStock);
        var warehouse = Assert.Single(view.Warehouses);
        Assert.Equal(15, warehouse.PhysicalStock);
        Assert.Equal(10, warehouse.Atp);
        Assert.Equal(3, warehouse.Hold);
        Assert.Equal(2, warehouse.Damaged);
        Assert.Equal(0, warehouse.Quarantine);
    }

    // 2-5 — ATP yalnız AVAILABLE - allocated; HOLD/QUARANTINE/DAMAGED ATP'ye girmez.
    [Fact]
    public async Task Atp_is_only_available_minus_allocated()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.Sku, world.LocA, InventoryStatus.Available, 10);
        await OpenStockAsync(world, world.Sku, world.LocA, InventoryStatus.Hold, 5);
        await OpenStockAsync(world, world.Sku, world.LocA, InventoryStatus.Quarantine, 4);
        await OpenStockAsync(world, world.Sku, world.LocA, InventoryStatus.Damaged, 3);
        await using var bundle = await CreateBundleAsync();

        await bundle.Contract.ReserveAsync(Guid.NewGuid(), world.Sku, world.Warehouse, 4, "test", CancellationToken.None);

        var view = await bundle.View.GetSkuAsync(world.Sku, CancellationToken.None);

        var wh = Assert.Single(view!.Warehouses);
        Assert.Equal(22, wh.PhysicalStock);
        Assert.Equal(6, wh.Atp);
        Assert.Equal(4, wh.Allocated);
        Assert.Equal(10, wh.Hold + wh.Quarantine + wh.Damaged - 2);
    }

    // 6-8 — Network physical/ATP warehouse toplamıdır; allocated çift sayılmaz.
    [Fact]
    public async Task Network_totals_are_warehouse_sums()
    {
        var world = await CreateWorldAsync();
        var warehouseB = await Db.CreateWarehouseAsync();
        var (locB1, _) = await Db.CreateStorageLocationAsync(warehouseB);
        var (locB2, _) = await Db.CreateStorageLocationAsync(warehouseB);

        await OpenStockAsync(world, world.Sku, world.LocA, InventoryStatus.Available, 10);
        await OpenStockAsync(world, world.Sku, world.LocB, InventoryStatus.Available, 5);
        await OpenStockAsync2(warehouseB, world.Sku, locB1, InventoryStatus.Available, 7);
        await OpenStockAsync2(warehouseB, world.Sku, locB2, InventoryStatus.Hold, 3);
        await using var bundle = await CreateBundleAsync();

        await bundle.Contract.ReserveAsync(Guid.NewGuid(), world.Sku, world.Warehouse, 4, "test", CancellationToken.None);
        await bundle.Contract.ReserveAsync(Guid.NewGuid(), world.Sku, warehouseB, 2, "test", CancellationToken.None);

        var view = await bundle.View.GetSkuAsync(world.Sku, CancellationToken.None);

        Assert.Equal(25, view!.NetworkPhysicalStock);
        Assert.Equal(16, view.NetworkAtp);
        Assert.Equal(6, view.NetworkAllocated);
        Assert.Equal(2, view.Warehouses.Count);

        var whA = view.Warehouses.Single(w => w.WarehouseId == world.Warehouse);
        Assert.Equal(15, whA.PhysicalStock);
        Assert.Equal(11, whA.Atp);
        var whB = view.Warehouses.Single(w => w.WarehouseId == warehouseB);
        Assert.Equal(10, whB.PhysicalStock);
        Assert.Equal(5, whB.Atp);
    }

    // 9 — Inactive warehouse operational candidate olarak işaretlenmez.
    [Fact]
    public async Task Inactive_warehouse_is_not_operational()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.Sku, world.LocA, InventoryStatus.Available, 8);
        await using (var facilityDb = Db.CreateFacilityContext())
        {
            var inactiveWarehouse = await facilityDb.Warehouses.FirstAsync(w => w.Id == world.Warehouse);
            inactiveWarehouse.Deactivate();
            await facilityDb.SaveChangesAsync();
        }

        await using var bundle = await CreateBundleAsync();
        var view = await bundle.View.GetSkuAsync(world.Sku, CancellationToken.None);

        var warehouse = Assert.Single(view!.Warehouses);
        Assert.False(warehouse.IsOperational);
        Assert.Equal(8, warehouse.PhysicalStock);
        Assert.Equal(8, warehouse.Atp);
    }

    // 10 — Location breakdown doğru.
    [Fact]
    public async Task Location_breakdown_is_correct()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.Sku, world.LocA, InventoryStatus.Available, 10);
        await OpenStockAsync(world, world.Sku, world.LocA, InventoryStatus.Hold, 2);
        await OpenStockAsync(world, world.Sku, world.LocB, InventoryStatus.Available, 4);
        await using var bundle = await CreateBundleAsync();

        await bundle.Contract.ReserveAsync(Guid.NewGuid(), world.Sku, world.Warehouse, 3, "test", CancellationToken.None);

        var rows = await bundle.Contract.ListSkuLocationBalancesAsync(world.Warehouse, world.Sku, CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.Equal(3, rows.Sum(r => r.Allocated));
        var locA_available = rows.Single(r => r.LocationId == world.LocA && r.Status == "AVAILABLE");
        Assert.Equal(10, locA_available.Quantity);
        Assert.Equal(10 - locA_available.Allocated, locA_available.Available);
        var locA_hold = rows.Single(r => r.LocationId == world.LocA && r.Status == "HOLD");
        Assert.Equal(2, locA_hold.Quantity);
        Assert.Equal(0, locA_hold.Allocated);
        var locB_available = rows.Single(r => r.LocationId == world.LocB);
        Assert.Equal(4, locB_available.Quantity);
        Assert.Equal(4 - locB_available.Allocated, locB_available.Available);
    }

    // 11 — Multi-warehouse SKU view doğru.
    [Fact]
    public async Task Multi_warehouse_sku_view_is_correct()
    {
        var world = await CreateWorldAsync();
        var warehouseB = await Db.CreateWarehouseAsync();
        var (locB, _) = await Db.CreateStorageLocationAsync(warehouseB);
        await OpenStockAsync(world, world.Sku, world.LocA, InventoryStatus.Available, 10);
        await OpenStockAsync2(warehouseB, world.Sku, locB, InventoryStatus.Available, 6);
        await using var bundle = await CreateBundleAsync();

        var view = await bundle.View.GetSkuAsync(world.Sku, CancellationToken.None);

        Assert.Equal(2, view!.Warehouses.Count);
        Assert.Equal(16, view.NetworkAtp);
        Assert.Contains(view.Warehouses, w => w.WarehouseId == world.Warehouse && w.Atp == 10);
        Assert.Contains(view.Warehouses, w => w.WarehouseId == warehouseB && w.Atp == 6);
        Assert.All(view.Warehouses, w => Assert.True(w.IsOperational));
    }

    // 12 — Multi-SKU batch query doğru.
    [Fact]
    public async Task Multi_sku_batch_availability_is_correct()
    {
        var world = await CreateWorldAsync();
        var (skuB, _) = await Db.CreateSkuWithBarcodeAsync("NETB");
        await OpenStockAsync(world, world.Sku, world.LocA, InventoryStatus.Available, 10);
        await OpenStockAsync(world, skuB, world.LocB, InventoryStatus.Available, 3);
        await using var bundle = await CreateBundleAsync();

        var lines = await bundle.View.GetOrderAvailabilityAsync(
            [new OrderAvailabilityLineInput(world.Sku, 5), new OrderAvailabilityLineInput(skuB, 5)],
            CancellationToken.None);

        Assert.Equal(2, lines.Count);
        var lineA = lines.Single(l => l.SkuId == world.Sku);
        Assert.True(lineA.IsSatisfiable);
        Assert.Equal(10, lineA.NetworkAtp);
        var warehouseA = Assert.Single(lineA.Warehouses);
        Assert.True(warehouseA.CanSatisfy);

        var lineB = lines.Single(l => l.SkuId == skuB);
        Assert.False(lineB.IsSatisfiable);
        Assert.Equal(3, lineB.NetworkAtp);
        var warehouseB = Assert.Single(lineB.Warehouses);
        Assert.False(warehouseB.CanSatisfy);
    }

    // 13-14 — Risk context read-only sunulur; quantity değişmez.
    [Fact]
    public async Task Risk_context_is_read_only_and_does_not_change_quantity()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.Sku, world.LocA, InventoryStatus.Available, 10);

        // Gerçekçi senaryo: stok 200 gündür hareket görmemiş (eski stok → risk RED).
        await using (var inventoryDb = Db.CreateInventoryContext())
        {
            await inventoryDb.Database.ExecuteSqlRawAsync(
                "UPDATE inventory.inventory_ledger SET occurred_at = now() - interval '200 days' WHERE warehouse_id = {0} AND sku_id = {1}",
                world.Warehouse,
                world.Sku);
        }

        await using var bundle = await CreateBundleAsync();

        await bundle.Contract.ReportPickNotFoundAsync(Guid.NewGuid(), world.Sku, world.Warehouse, world.LocA, null, CancellationToken.None);
        await bundle.Contract.ReportPickNotFoundAsync(Guid.NewGuid(), world.Sku, world.Warehouse, world.LocA, null, CancellationToken.None);

        var before = await bundle.View.GetSkuAsync(world.Sku, CancellationToken.None);
        var risk = await bundle.Contract.GetWarehouseSkuRiskAsync(world.Warehouse, world.Sku, CancellationToken.None);

        Assert.NotNull(risk);
        Assert.Equal("RED", risk!.RiskLevel);
        Assert.True(risk.RiskScore >= 80);
        Assert.Equal(10, before!.NetworkPhysicalStock);
        Assert.Equal(10, before.NetworkAtp);

        var warehouse = Assert.Single(before.Warehouses);
        Assert.Equal("RED", warehouse.RiskLevel);
        Assert.Equal(10, warehouse.Atp);
    }

    // 15 — Inventory mutation sonrası network view yeni değeri gösterir.
    [Fact]
    public async Task Network_view_reflects_stock_mutations_immediately()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.Sku, world.LocA, InventoryStatus.Available, 10);
        await using var bundle = await CreateBundleAsync();

        var before = await bundle.View.GetSkuAsync(world.Sku, CancellationToken.None);
        Assert.Equal(10, before!.NetworkAtp);

        await bundle.Contract.ReceiveInventoryAsync(
            new Wms.Modules.Inventory.Contracts.ReceiveInventoryCommand(Guid.NewGuid(), world.Sku, world.Warehouse, world.LocB, "AVAILABLE", 5, "TEST", null),
            CancellationToken.None);

        var after = await bundle.View.GetSkuAsync(world.Sku, CancellationToken.None);
        Assert.Equal(15, after!.NetworkAtp);
        Assert.Equal(15, after.NetworkPhysicalStock);
    }

    // 16-19 — Network için duplicate mutable stock tablosu yok; fulfillment yalnız audit tabloları tutar.
    [Fact]
    public async Task Network_view_creates_no_duplicate_mutable_stock_tables()
    {
        await using var db = Db.CreateInventoryContext();
        var rows = await db.Database.SqlQueryRaw<TableRow>(
                """
                SELECT table_schema AS "schema", table_name AS "name"
                FROM information_schema.tables
                WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
                  AND table_name ILIKE '%network%'
                """)
            .ToListAsync();
        Assert.Empty(rows);

        var fulfillmentTables = await db.Database.SqlQueryRaw<TableRow>(
                """
                SELECT table_schema AS "schema", table_name AS "name"
                FROM information_schema.tables
                WHERE table_schema = 'fulfillment'
                  AND (table_name ILIKE '%stock%' OR table_name ILIKE '%balance%' OR table_name ILIKE '%inventory%')
                """)
            .ToListAsync();
        Assert.Empty(fulfillmentTables);
    }

    // 17 — Large dataset DB aggregation (çoklu SKU/warehouse doğruluğu).
    [Fact]
    public async Task Large_dataset_aggregation_is_correct()
    {
        var warehouses = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            warehouses.Add(await Db.CreateWarehouseAsync());
        }

        var skus = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            skus.Add(await Db.CreateSkuAsync());
        }

        var expectedPhysical = 0;
        var expectedAtp = 0;
        var index = 0;
        foreach (var warehouse in warehouses)
        {
            foreach (var sku in skus)
            {
                var (loc, _) = await Db.CreateStorageLocationAsync(warehouse);
                var available = 5 + (index % 10);
                var hold = 2;
                index++;
                await OpenStockAsync2(warehouse, sku, loc, InventoryStatus.Available, available);
                await OpenStockAsync2(warehouse, sku, loc, InventoryStatus.Hold, hold);
                expectedPhysical += available + hold;
                expectedAtp += available;
            }
        }

        await using var bundle = await CreateBundleAsync();
        var summary = await bundle.View.GetSummaryAsync(CancellationToken.None);

        var totalPhysical = warehouses
            .Select(w => summary.Warehouses.FirstOrDefault(r => r.WarehouseId == w))
            .Sum(r => r?.PhysicalStock ?? 0);
        var totalAtp = warehouses
            .Select(w => summary.Warehouses.FirstOrDefault(r => r.WarehouseId == w))
            .Sum(r => r?.Atp ?? 0);

        Assert.Equal(expectedPhysical, totalPhysical);
        Assert.Equal(expectedAtp, totalAtp);
        Assert.Equal(30, warehouses.Sum(w => summary.Warehouses.First(r => r.WarehouseId == w).SkuCount));
    }

    private static async Task OpenStockAsync2(Guid warehouseId, Guid skuId, Guid locationId, InventoryStatus status, int quantity)
    {
        await using var inventoryDb = Db.CreateInventoryContext();
        await using var facilityDb = Db.CreateFacilityContext();
        await using var masterDb = Db.CreateMasterDataContext();
        var store = new InventoryStore(inventoryDb);
        var opening = new RecordOpeningBalance(
            store,
            new MasterDataQueryContract(masterDb),
            new FacilityQueryContract(facilityDb));
        await opening.Handle(
            new RecordOpeningBalanceCommand(Guid.NewGuid(), skuId, warehouseId, locationId, status, quantity),
            CancellationToken.None);
    }

    // 18 — Pagination çalışır.
    [Fact]
    public async Task Warehouse_view_pagination_works()
    {
        var world = await CreateWorldAsync();
        for (var i = 0; i < 12; i++)
        {
            var sku = await Db.CreateSkuAsync();
            var (loc, _) = await Db.CreateStorageLocationAsync(world.Warehouse);
            await OpenStockAsync2(world.Warehouse, sku, loc, InventoryStatus.Available, 3);
        }

        await using var bundle = await CreateBundleAsync();

        var page1 = await bundle.View.GetWarehouseAsync(world.Warehouse, 1, 5, CancellationToken.None);
        var page2 = await bundle.View.GetWarehouseAsync(world.Warehouse, 2, 5, CancellationToken.None);
        var page3 = await bundle.View.GetWarehouseAsync(world.Warehouse, 3, 5, CancellationToken.None);

        Assert.Equal(5, page1!.Skus.Count);
        Assert.Equal(5, page2!.Skus.Count);
        Assert.Equal(2, page3!.Skus.Count);
        Assert.Equal(12, page1.SkuCount);
        Assert.Equal(12, page2.SkuCount);
        Assert.Equal(12, page3.SkuCount);

        var all = page1.Skus.Concat(page2.Skus).Concat(page3.Skus).Select(s => s.SkuId).ToList();
        Assert.Equal(12, all.Distinct().Count());
    }

    // 19b — Network sku listesi filtre/sıralama çalışır.
    [Fact]
    public async Task Sku_list_filters_and_sorting_work()
    {
        var world = await CreateWorldAsync();
        var (skuBig, _) = await Db.CreateSkuWithBarcodeAsync("BIG");
        var (skuSmall, _) = await Db.CreateSkuWithBarcodeAsync("SML");
        await OpenStockAsync2(world.Warehouse, skuBig, world.LocA, InventoryStatus.Available, 40);
        await OpenStockAsync2(world.Warehouse, skuSmall, world.LocB, InventoryStatus.Available, 4);
        await OpenStockAsync2(world.Warehouse, world.Sku, world.LocB, InventoryStatus.Hold, 9);
        await using var bundle = await CreateBundleAsync();

        var atpPage = await bundle.View.ListSkusAsync(
            new ListNetworkSkusFilter(world.Warehouse, null, true, null, null, "atp", 1, 10),
            CancellationToken.None);
        Assert.Equal(2, atpPage.Total);
        Assert.Equal(skuBig, atpPage.Rows[0].SkuId);
        Assert.Equal(40, atpPage.Rows[0].Atp);

        var stockPage = await bundle.View.ListSkusAsync(
            new ListNetworkSkusFilter(world.Warehouse, true, null, null, null, "physical", 1, 10),
            CancellationToken.None);
        Assert.Equal(3, stockPage.Total);

        var searchPage = await bundle.View.ListSkusAsync(
            new ListNetworkSkusFilter(world.Warehouse, null, null, null, "SML", null, 1, 10),
            CancellationToken.None);
        Assert.Equal(1, searchPage.Total);
        Assert.Equal(skuSmall, searchPage.Rows[0].SkuId);
    }

    private sealed class TableRow
    {
        public string Schema { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }

    private sealed record World(Guid Sku, string Barcode, Guid Warehouse, Guid LocA, Guid LocB);

    private sealed class Bundle : IAsyncDisposable
    {
        private readonly FacilityDbContext _facilityDb;
        private readonly InventoryDbContext _inventoryDb;
        private readonly MasterDataDbContext _masterDb;

        public Bundle(
            InventoryStore inventoryStore,
            IInventoryContract contract,
            NetworkInventoryView view,
            FacilityDbContext facilityDb,
            InventoryDbContext inventoryDb,
            MasterDataDbContext masterDb)
        {
            InventoryStore = inventoryStore;
            Contract = contract;
            View = view;
            _facilityDb = facilityDb;
            _inventoryDb = inventoryDb;
            _masterDb = masterDb;
        }

        public InventoryStore InventoryStore { get; }

        public IInventoryContract Contract { get; }

        public NetworkInventoryView View { get; }

        public async ValueTask DisposeAsync()
        {
            await _facilityDb.DisposeAsync();
            await _inventoryDb.DisposeAsync();
            await _masterDb.DisposeAsync();
        }
    }
}
