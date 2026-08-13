using Microsoft.EntityFrameworkCore;
using Wms.Modules.Facility.Domain;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Application.Accuracy.Scanning;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.MasterData.Infrastructure.Persistence;
using Xunit;

namespace Wms.InventoryTests.Persistence;

public sealed class ScannedRelocationTests
{
    private static async Task<ScanWorld> CreateScanWorldAsync(int openingQuantity = 100, bool openBalanceAtSource = true)
    {
        var (sku, barcode) = await Db.CreateSkuWithBarcodeAsync();
        var (warehouse, source, sourceCode) = await Db.CreateWarehouseWithStorageLocationWithCodeAsync();
        var (destination, destinationCode) = await Db.CreateLocationAsync(warehouse, LocationType.Storage, holdsInventory: true);

        if (openBalanceAtSource)
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
                new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, source, InventoryStatus.Available, openingQuantity),
                CancellationToken.None);
        }

        return new ScanWorld(sku, barcode, warehouse, source, sourceCode, destination, destinationCode);
    }

    private static async Task<ScanContext> CreateScanContextAsync(ScanWorld world)
    {
        var inventoryDb = Db.CreateInventoryContext();
        var facilityDb = Db.CreateFacilityContext();
        var masterDb = Db.CreateMasterDataContext();
        var store = new InventoryStore(inventoryDb);
        var relocate = new RelocateStock(
            store,
            new MasterDataQueryContract(masterDb),
            new FacilityQueryContract(facilityDb));
        var useCase = new ExecuteScannedRelocation(
            store,
            new MasterDataQueryContract(masterDb),
            new FacilityQueryContract(facilityDb),
            relocate);
        return new ScanContext(store, useCase, inventoryDb, facilityDb, masterDb);
    }

    private static ScannedRelocationCommand BuildCommand(ScanWorld world, int quantity, Guid? requestId = null) =>
        new(
            requestId ?? Guid.NewGuid(),
            world.Warehouse,
            world.SourceCode,
            world.Barcode,
            world.DestinationCode,
            quantity,
            "RF-01",
            "operator-7");

    // 1 — Valid scans relocate stock and record evidence atomically.
    [Fact]
    public async Task Valid_scans_relocate_stock_and_record_evidence()
    {
        var world = await CreateScanWorldAsync(100);
        await using var context = await CreateScanContextAsync(world);

        var result = await context.UseCase.Handle(BuildCommand(world, 30), CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Completed, result.Status);
        Assert.NotNull(result.MovementId);
        Assert.NotNull(result.EvidenceId);

        var sourceBalance = await context.Store.GetBalanceAsync(world.Warehouse, world.Sku, world.Source, InventoryStatus.Available, CancellationToken.None);
        var destBalance = await context.Store.GetBalanceAsync(world.Warehouse, world.Sku, world.Destination, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(70, sourceBalance!.Quantity);
        Assert.Equal(30, destBalance!.Quantity);

        var evidence = await context.Store.GetScanEvidenceByMovementIdAsync(result.MovementId!.Value, CancellationToken.None);
        Assert.NotNull(evidence);
        Assert.Equal(result.MovementId, evidence!.MovementId);
        Assert.Equal(world.SourceCode, evidence.SourceScanValue);
        Assert.Equal(world.Barcode, evidence.SkuScanValue);
        Assert.Equal(world.DestinationCode, evidence.DestinationScanValue);
        Assert.Equal(30, evidence.Quantity);
        Assert.Equal("RF-01", evidence.DeviceId);
        Assert.Equal("operator-7", evidence.OperatorId);
    }

    // 2 — Missing source scan rejected (strict mode).
    [Fact]
    public async Task Missing_source_scan_is_rejected()
    {
        var world = await CreateScanWorldAsync(100);
        await using var context = await CreateScanContextAsync(world);

        var result = await context.UseCase.Handle(
            new ScannedRelocationCommand(Guid.NewGuid(), world.Warehouse, "  ", world.Barcode, world.DestinationCode, 10),
            CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, result.Status);
        Assert.Equal(ScanRejectionCode.ScanRequired, result.RejectionCode);
    }

    // 3 — Missing SKU barcode scan rejected (strict mode).
    [Fact]
    public async Task Missing_sku_scan_is_rejected()
    {
        var world = await CreateScanWorldAsync(100);
        await using var context = await CreateScanContextAsync(world);

        var result = await context.UseCase.Handle(
            new ScannedRelocationCommand(Guid.NewGuid(), world.Warehouse, world.SourceCode, string.Empty, world.DestinationCode, 10),
            CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, result.Status);
        Assert.Equal(ScanRejectionCode.ScanRequired, result.RejectionCode);
    }

    // 4 — Missing destination scan rejected (strict mode).
    [Fact]
    public async Task Missing_destination_scan_is_rejected()
    {
        var world = await CreateScanWorldAsync(100);
        await using var context = await CreateScanContextAsync(world);

        var result = await context.UseCase.Handle(
            new ScannedRelocationCommand(Guid.NewGuid(), world.Warehouse, world.SourceCode, world.Barcode, string.Empty, 10),
            CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, result.Status);
        Assert.Equal(ScanRejectionCode.ScanRequired, result.RejectionCode);
    }

    // 5 — Unknown source location code → SOURCE_NOT_FOUND.
    [Fact]
    public async Task Unknown_source_code_is_rejected_with_SourceNotFound()
    {
        var world = await CreateScanWorldAsync(100);
        await using var context = await CreateScanContextAsync(world);

        var result = await context.UseCase.Handle(
            new ScannedRelocationCommand(Guid.NewGuid(), world.Warehouse, "NO-SUCH-LOC", world.Barcode, world.DestinationCode, 10),
            CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, result.Status);
        Assert.Equal(ScanRejectionCode.SourceNotFound, result.RejectionCode);
    }

    // 6 — Unknown barcode → SKU_NOT_FOUND.
    [Fact]
    public async Task Unknown_barcode_is_rejected_with_SkuNotFound()
    {
        var world = await CreateScanWorldAsync(100);
        await using var context = await CreateScanContextAsync(world);

        var result = await context.UseCase.Handle(
            new ScannedRelocationCommand(Guid.NewGuid(), world.Warehouse, world.SourceCode, "UNKNOWN-BC-999", world.DestinationCode, 10),
            CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, result.Status);
        Assert.Equal(ScanRejectionCode.SkuNotFound, result.RejectionCode);
    }

    // 7 — Unknown destination location code → DESTINATION_NOT_FOUND.
    [Fact]
    public async Task Unknown_destination_code_is_rejected_with_DestinationNotFound()
    {
        var world = await CreateScanWorldAsync(100);
        await using var context = await CreateScanContextAsync(world);

        var result = await context.UseCase.Handle(
            new ScannedRelocationCommand(Guid.NewGuid(), world.Warehouse, world.SourceCode, world.Barcode, "NO-SUCH-DEST", 10),
            CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, result.Status);
        Assert.Equal(ScanRejectionCode.DestinationNotFound, result.RejectionCode);
    }

    // 8 — Inactive source location → LOCATION_INACTIVE.
    [Fact]
    public async Task Inactive_source_location_is_rejected_with_LocationInactive()
    {
        var world = await CreateScanWorldAsync(100);
        await using (var facilityDb = Db.CreateFacilityContext())
        {
            var location = await facilityDb.Locations.FirstAsync(l => l.Id == world.Source);
            location.Deactivate();
            await facilityDb.SaveChangesAsync();
        }

        await using var context = await CreateScanContextAsync(world);
        var result = await context.UseCase.Handle(BuildCommand(world, 10), CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, result.Status);
        Assert.Equal(ScanRejectionCode.LocationInactive, result.RejectionCode);
    }

    // 9 — Inactive destination location → LOCATION_INACTIVE.
    [Fact]
    public async Task Inactive_destination_location_is_rejected_with_LocationInactive()
    {
        var world = await CreateScanWorldAsync(100);
        await using (var facilityDb = Db.CreateFacilityContext())
        {
            var location = await facilityDb.Locations.FirstAsync(l => l.Id == world.Destination);
            location.Deactivate();
            await facilityDb.SaveChangesAsync();
        }

        await using var context = await CreateScanContextAsync(world);
        var result = await context.UseCase.Handle(BuildCommand(world, 10), CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, result.Status);
        Assert.Equal(ScanRejectionCode.LocationInactive, result.RejectionCode);
    }

    // 10 — Destination in another warehouse → WRONG_WAREHOUSE.
    [Fact]
    public async Task Destination_in_other_warehouse_is_rejected_with_WrongWarehouse()
    {
        var world = await CreateScanWorldAsync(100);
        var (otherWarehouse, _) = await Db.CreateWarehouseWithStorageLocationAsync();
        var (foreignLocation, foreignCode) = await Db.CreateLocationAsync(otherWarehouse, LocationType.Storage, holdsInventory: true);
        Assert.True(foreignLocation != Guid.Empty);

        await using var context = await CreateScanContextAsync(world);
        var result = await context.UseCase.Handle(
            new ScannedRelocationCommand(Guid.NewGuid(), world.Warehouse, world.SourceCode, world.Barcode, foreignCode, 10),
            CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, result.Status);
        Assert.Equal(ScanRejectionCode.WrongWarehouse, result.RejectionCode);
    }

    // 11 — SHIPPING type destination → DESTINATION_NOT_ALLOWED.
    [Fact]
    public async Task Shipping_type_destination_is_rejected_with_DestinationNotAllowed()
    {
        var world = await CreateScanWorldAsync(100);
        var (shippingLocation, shippingCode) = await Db.CreateLocationAsync(world.Warehouse, LocationType.Shipping, holdsInventory: true);

        await using var context = await CreateScanContextAsync(world);
        var result = await context.UseCase.Handle(
            new ScannedRelocationCommand(Guid.NewGuid(), world.Warehouse, world.SourceCode, world.Barcode, shippingCode, 10),
            CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, result.Status);
        Assert.Equal(ScanRejectionCode.DestinationNotAllowed, result.RejectionCode);
        Assert.True(shippingLocation != Guid.Empty);
    }

    // 12 — Destination that cannot hold inventory → DESTINATION_NOT_ALLOWED.
    [Fact]
    public async Task Destination_without_inventory_capacity_is_rejected_with_DestinationNotAllowed()
    {
        var world = await CreateScanWorldAsync(100);
        var (dock, dockCode) = await Db.CreateLocationAsync(world.Warehouse, LocationType.Dock, holdsInventory: false);

        await using var context = await CreateScanContextAsync(world);
        var result = await context.UseCase.Handle(
            new ScannedRelocationCommand(Guid.NewGuid(), world.Warehouse, world.SourceCode, world.Barcode, dockCode, 10),
            CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, result.Status);
        Assert.Equal(ScanRejectionCode.DestinationNotAllowed, result.RejectionCode);
        Assert.True(dock != Guid.Empty);
    }

    // 13 — SKU not at source → SKU_NOT_AT_SOURCE.
    [Fact]
    public async Task Sku_not_at_source_is_rejected_with_SkuNotAtSource()
    {
        var world = await CreateScanWorldAsync(openBalanceAtSource: false);
        await using var context = await CreateScanContextAsync(world);

        var result = await context.UseCase.Handle(BuildCommand(world, 10), CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, result.Status);
        Assert.Equal(ScanRejectionCode.SkuNotAtSource, result.RejectionCode);
    }

    // 14 — Insufficient available stock → INSUFFICIENT_AVAILABLE_STOCK.
    [Fact]
    public async Task Insufficient_available_stock_is_rejected()
    {
        var world = await CreateScanWorldAsync(10);
        await using var context = await CreateScanContextAsync(world);

        var result = await context.UseCase.Handle(BuildCommand(world, 11), CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, result.Status);
        Assert.Equal(ScanRejectionCode.InsufficientAvailableStock, result.RejectionCode);
        Assert.Equal(10, (await context.Store.GetBalanceAsync(world.Warehouse, world.Sku, world.Source, InventoryStatus.Available, CancellationToken.None))!.Quantity);
    }

    // 15 — Allocated stock is protected.
    [Fact]
    public async Task Allocated_stock_is_protected_from_scanned_relocation()
    {
        var world = await CreateScanWorldAsync(10);
        await using var context = await CreateScanContextAsync(world);

        await using var reserveDb = Db.CreateInventoryContext();
        await using var facilityDb = Db.CreateFacilityContext();
        await using var masterDb = Db.CreateMasterDataContext();
        var reserveStore = new InventoryStore(reserveDb);
        var reserve = new Reserve(reserveStore, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
        await reserve.Handle(new ReserveCommand(Guid.NewGuid(), world.Sku, world.Warehouse, 8, "hold"), CancellationToken.None);

        var result = await context.UseCase.Handle(BuildCommand(world, 5), CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, result.Status);
        Assert.Equal(ScanRejectionCode.InsufficientAvailableStock, result.RejectionCode);
        Assert.Equal(8, (await context.Store.GetBalanceAsync(world.Warehouse, world.Sku, world.Source, InventoryStatus.Available, CancellationToken.None))!.Allocated);
    }

    // 16 — Duplicate RequestId: no second movement, single evidence.
    [Fact]
    public async Task Duplicate_request_id_returns_DuplicateRequest_without_second_movement()
    {
        var world = await CreateScanWorldAsync(100);
        await using var context = await CreateScanContextAsync(world);
        var requestId = Guid.NewGuid();

        var first = await context.UseCase.Handle(BuildCommand(world, 20, requestId), CancellationToken.None);
        var second = await context.UseCase.Handle(BuildCommand(world, 20, requestId), CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Completed, first.Status);
        Assert.Equal(ScannedRelocationStatus.DuplicateRequest, second.Status);
        Assert.Equal(first.MovementId, second.MovementId);

        var movements = await context.Store.ListMovementsAsync(world.Warehouse, world.Sku, 50, CancellationToken.None);
        Assert.Single(movements, m => m.RequestId == requestId);
        Assert.Equal(80, (await context.Store.GetBalanceAsync(world.Warehouse, world.Sku, world.Source, InventoryStatus.Available, CancellationToken.None))!.Quantity);

        var evidence = await context.Store.GetScanEvidenceByMovementIdAsync(first.MovementId!.Value, CancellationToken.None);
        Assert.NotNull(evidence);
    }

    // 17 — Concurrent duplicate scan requests: one movement, one evidence.
    [Fact]
    public async Task Concurrent_duplicate_scan_requests_produce_single_movement_and_evidence()
    {
        var world = await CreateScanWorldAsync(100);
        var requestId = Guid.NewGuid();

        var results = await Task.WhenAll(
            RunScanAsync(world, requestId, 30),
            RunScanAsync(world, requestId, 30));

        Assert.Equal(1, results.Count(r => r.Status == ScannedRelocationStatus.Completed));
        Assert.Equal(1, results.Count(r => r.Status == ScannedRelocationStatus.DuplicateRequest));

        await using var verifyDb = Db.CreateInventoryContext();
        var verifyStore = new InventoryStore(verifyDb);
        var movements = await verifyStore.ListMovementsAsync(world.Warehouse, world.Sku, 50, CancellationToken.None);
        var matching = movements.Where(m => m.RequestId == requestId).ToList();
        var movement = Assert.Single(matching);

        var evidence = await verifyStore.GetScanEvidenceByMovementIdAsync(movement.Id, CancellationToken.None);
        Assert.NotNull(evidence);

        var sourceBalance = await verifyStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Source, InventoryStatus.Available, CancellationToken.None);
        var destBalance = await verifyStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Destination, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(70, sourceBalance!.Quantity);
        Assert.Equal(30, destBalance!.Quantity);
    }

    private static async Task<ScannedRelocationResult> RunScanAsync(ScanWorld world, Guid requestId, int quantity)
    {
        await using var context = await CreateScanContextAsync(world);
        return await context.UseCase.Handle(BuildCommand(world, quantity, requestId), CancellationToken.None);
    }

    // 18 — Warehouse physical total unchanged by scanned relocation.
    [Fact]
    public async Task Scanned_relocation_preserves_warehouse_physical_total()
    {
        var world = await CreateScanWorldAsync(100);
        await using var context = await CreateScanContextAsync(world);
        var summary = new GetWarehouseSkuSummary(context.Store);

        var before = await summary.Handle(world.Warehouse, world.Sku, CancellationToken.None);
        var result = await context.UseCase.Handle(BuildCommand(world, 45), CancellationToken.None);
        var after = await summary.Handle(world.Warehouse, world.Sku, CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Completed, result.Status);
        Assert.Equal(before.OnHand, after.OnHand);
        Assert.Equal(100, after.OnHand);
    }

    // 19 — Rejected scan leaves no movement and no evidence.
    [Fact]
    public async Task Rejected_scan_leaves_no_movement_or_evidence()
    {
        var world = await CreateScanWorldAsync(100);
        await using var context = await CreateScanContextAsync(world);

        var rejected = await context.UseCase.Handle(
            new ScannedRelocationCommand(Guid.NewGuid(), world.Warehouse, world.SourceCode, "BAD-BARCODE", world.DestinationCode, 10),
            CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Rejected, rejected.Status);

        await using var verifyDb = Db.CreateInventoryContext();
        var verifyStore = new InventoryStore(verifyDb);
        var movements = await verifyStore.ListMovementsAsync(world.Warehouse, world.Sku, 50, CancellationToken.None);
        Assert.Empty(movements);
        Assert.Equal(100, (await verifyStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Source, InventoryStatus.Available, CancellationToken.None))!.Quantity);
    }

    // 20 — Evidence and movement are stored in the same transaction (atomicity).
    [Fact]
    public async Task Evidence_and_movement_are_atomic()
    {
        var world = await CreateScanWorldAsync(100);
        await using var context = await CreateScanContextAsync(world);

        var result = await context.UseCase.Handle(BuildCommand(world, 25), CancellationToken.None);

        Assert.Equal(ScannedRelocationStatus.Completed, result.Status);

        await using var verifyDb = Db.CreateInventoryContext();
        var evidenceCount = await verifyDb.ScanMovementEvidences.CountAsync(e => e.MovementId == result.MovementId);
        var movementCount = await verifyDb.InventoryMovements.CountAsync(m => m.Id == result.MovementId);
        Assert.Equal(1, evidenceCount);
        Assert.Equal(1, movementCount);
    }

    // 21 — Evidence persisted in PostgreSQL with all scan values (real persistence round-trip).
    [Fact]
    public async Task Evidence_persists_in_postgres_with_all_scan_values()
    {
        var world = await CreateScanWorldAsync(100);
        await using var context = await CreateScanContextAsync(world);

        var result = await context.UseCase.Handle(BuildCommand(world, 15), CancellationToken.None);

        await using var verifyDb = Db.CreateInventoryContext();
        var row = await verifyDb.ScanMovementEvidences
            .AsNoTracking()
            .SingleAsync(e => e.Id == result.EvidenceId);

        Assert.Equal(world.SourceCode, row.SourceScanValue);
        Assert.Equal(world.Barcode, row.SkuScanValue);
        Assert.Equal(world.DestinationCode, row.DestinationScanValue);
        Assert.Equal(15, row.Quantity);
        Assert.Equal(result.MovementId, row.MovementId);
        Assert.NotEqual(default, row.OccurredAt);
    }

    private sealed record ScanWorld(
        Guid Sku,
        string Barcode,
        Guid Warehouse,
        Guid Source,
        string SourceCode,
        Guid Destination,
        string DestinationCode);

    private sealed class ScanContext : IAsyncDisposable
    {
        private readonly InventoryDbContext _inventoryDb;
        private readonly FacilityDbContext _facilityDb;
        private readonly MasterDataDbContext _masterDb;

        public ScanContext(
            InventoryStore store,
            ExecuteScannedRelocation useCase,
            InventoryDbContext inventoryDb,
            FacilityDbContext facilityDb,
            MasterDataDbContext masterDb)
        {
            Store = store;
            UseCase = useCase;
            _inventoryDb = inventoryDb;
            _facilityDb = facilityDb;
            _masterDb = masterDb;
        }

        public InventoryStore Store { get; }

        public ExecuteScannedRelocation UseCase { get; }

        public async ValueTask DisposeAsync()
        {
            await _inventoryDb.DisposeAsync();
            await _facilityDb.DisposeAsync();
            await _masterDb.DisposeAsync();
        }
    }
}
