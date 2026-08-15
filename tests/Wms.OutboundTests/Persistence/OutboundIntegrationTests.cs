using Microsoft.EntityFrameworkCore;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Application.Accuracy;
using Wms.Modules.Inventory.Application.Accuracy.CycleCounting;
using Wms.Modules.Inventory.Application.Accuracy.Scanning;
using Wms.Modules.Inventory.Contracts;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Domain.Accuracy;
using Wms.Modules.Inventory.Domain.Accuracy.CycleCounting;
using Wms.Modules.Inventory.Infrastructure;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.MasterData.Infrastructure.Persistence;
using Wms.Modules.Outbound.Application;
using Wms.Modules.Outbound.Domain;
using Wms.Modules.Outbound.Infrastructure.Persistence;
using Xunit;

namespace Wms.OutboundTests.Persistence;

public sealed class OutboundIntegrationTests
{
    private static async Task<World> CreateWorldAsync()
    {
        var (skuA, barcodeA) = await Db.CreateSkuWithBarcodeAsync();
        var (skuB, barcodeB) = await Db.CreateSkuWithBarcodeAsync();
        var warehouse = await Db.CreateWarehouseAsync();
        var (locA, codeA) = await Db.CreateStorageLocationAsync(warehouse);
        var (locB, codeB) = await Db.CreateStorageLocationAsync(warehouse);
        return new World(skuA, barcodeA, skuB, barcodeB, warehouse, locA, codeA, locB, codeB);
    }

    private static async Task<Bundle> CreateBundleAsync(World world)
    {
        var outboundDb = Db.CreateOutboundContext();
        var inventoryDb = Db.CreateInventoryContext();
        var facilityDb = Db.CreateFacilityContext();
        var masterDb = Db.CreateMasterDataContext();

        var outboundStore = new OutboundStore(outboundDb);
        var inventoryStore = new InventoryStore(inventoryDb);
        var facilityContract = new FacilityQueryContract(facilityDb);
        var masterContract = new MasterDataQueryContract(masterDb);

        var inventoryContract = new InventoryContractAdapter(
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
            outboundStore,
            inventoryStore,
            inventoryContract,
            new CreateFulfillmentOrder(outboundStore, masterContract, facilityContract),
            new AllocateOrder(outboundStore, inventoryContract),
            new StartPick(outboundStore),
            new ConfirmPick(outboundStore, masterContract, facilityContract),
            new MarkPickNotFound(outboundStore, inventoryContract),
            new PackOrder(outboundStore),
            new ShipOrder(outboundStore, inventoryContract),
            new CancelOrder(outboundStore, inventoryContract),
            new GetOrder(outboundStore),
            new ListOrders(outboundStore),
            new GetPickTask(outboundStore),
            new ListPickTasks(outboundStore),
            outboundDb,
            inventoryDb,
            facilityDb,
            masterDb);
    }

    private static async Task OpenStockAsync(World world, Guid skuId, Guid locationId, int quantity)
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
            new RecordOpeningBalanceCommand(Guid.NewGuid(), skuId, world.Warehouse, locationId, InventoryStatus.Available, quantity),
            CancellationToken.None);
    }

    private static async Task<Guid> CreateOrderAsync(Bundle bundle, World world, int qtyA, int qtyB)
    {
        var result = await bundle.CreateOrder.Handle(
            new CreateFulfillmentOrderCommand(
                Guid.NewGuid(),
                null,
                world.Warehouse,
                "EXT-REF-1",
                [
                    new CreateFulfillmentOrderLineInput(world.SkuA, qtyA),
                    new CreateFulfillmentOrderLineInput(world.SkuB, qtyB),
                ]),
            CancellationToken.None);
        return result.OrderId;
    }

    // 1 — Order oluşturulur.
    [Fact]
    public async Task Order_is_created()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);

        var orderId = await CreateOrderAsync(bundle, world, 2, 4);
        var order = await bundle.GetOrder.Handle(orderId, CancellationToken.None);

        Assert.NotNull(order);
        Assert.Equal(OrderStatus.Created, order!.Status);
        Assert.StartsWith("OUT-", order.OrderNumber);
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal("EXT-REF-1", order.ExternalOrderReference);
        Assert.All(order.Lines, l => Assert.Null(l.ReservationId));
    }

    // 2 — Invalid SKU reddedilir.
    [Fact]
    public async Task Invalid_sku_is_rejected()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);

        await Assert.ThrowsAsync<InvalidOrderStateException>(() => bundle.CreateOrder.Handle(
            new CreateFulfillmentOrderCommand(Guid.NewGuid(), null, world.Warehouse, null,
                [new CreateFulfillmentOrderLineInput(Guid.NewGuid(), 5)]),
            CancellationToken.None));

        await using (var masterDb = Db.CreateMasterDataContext())
        {
            var sku = await masterDb.Skus.FirstAsync(s => s.Id == world.SkuA);
            sku.Deactivate();
            await masterDb.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOrderStateException>(() => bundle.CreateOrder.Handle(
            new CreateFulfillmentOrderCommand(Guid.NewGuid(), null, world.Warehouse, null,
                [new CreateFulfillmentOrderLineInput(world.SkuA, 5)]),
            CancellationToken.None));
    }

    // 3 — Invalid/inactive warehouse reddedilir.
    [Fact]
    public async Task Invalid_warehouse_is_rejected()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);

        await Assert.ThrowsAsync<InvalidOrderStateException>(() => bundle.CreateOrder.Handle(
            new CreateFulfillmentOrderCommand(Guid.NewGuid(), null, Guid.NewGuid(), null,
                [new CreateFulfillmentOrderLineInput(world.SkuA, 5)]),
            CancellationToken.None));
    }

    // 4 — Allocation Inventory contract kullanır.
    [Fact]
    public async Task Allocation_uses_inventory_contract()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.SkuA, world.LocA, 10);
        await OpenStockAsync(world, world.SkuB, world.LocA, 20);
        await using var bundle = await CreateBundleAsync(world);

        var orderId = await CreateOrderAsync(bundle, world, 3, 4);
        var result = await bundle.AllocateOrder.Handle(orderId, CancellationToken.None);

        Assert.Equal(AllocateOrderOutcome.Allocated, result.Outcome);
        var order = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
        Assert.Equal(OrderStatus.Allocated, order!.Status);
        Assert.NotNull(order.AllocatedAt);
        Assert.All(order.Lines, l => Assert.NotNull(l.ReservationId));

        var reservationDetail = await bundle.InventoryContract.GetReservationAsync(order.Lines[0].ReservationId!.Value, CancellationToken.None);
        Assert.NotNull(reservationDetail);
        Assert.Equal("ALLOCATED", reservationDetail!.Status);

        var balance = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.SkuA, world.LocA, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(3, balance!.Allocated);
    }

    // 5 — Multi-line allocation all-or-nothing.
    [Fact]
    public async Task Multi_line_allocation_is_all_or_nothing()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.SkuA, world.LocA, 10);
        // skuB için stok YOK.
        await using var bundle = await CreateBundleAsync(world);

        var orderId = await CreateOrderAsync(bundle, world, 3, 4);
        var result = await bundle.AllocateOrder.Handle(orderId, CancellationToken.None);

        Assert.Equal(AllocateOrderOutcome.InsufficientStock, result.Outcome);
        var order = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
        Assert.Equal(OrderStatus.AllocationFailed, order!.Status);
        Assert.All(order.Lines, l => Assert.Null(l.ReservationId));

        // Dangling reservation YOK: skuA allocated 0 kalır.
        var balance = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.SkuA, world.LocA, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(0, balance!.Allocated);

        await using var verifyDb = Db.CreateInventoryContext();
        var reservations = await verifyDb.InventoryReservations.CountAsync(r => r.WarehouseId == world.Warehouse);
        Assert.Equal(0, reservations);
    }

    // 6 — Duplicate allocation stok iki kez reserve etmez.
    [Fact]
    public async Task Duplicate_allocation_does_not_double_reserve()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.SkuA, world.LocA, 10);
        await OpenStockAsync(world, world.SkuB, world.LocA, 20);
        await using var bundle = await CreateBundleAsync(world);

        var orderId = await CreateOrderAsync(bundle, world, 3, 4);
        var first = await bundle.AllocateOrder.Handle(orderId, CancellationToken.None);
        var second = await bundle.AllocateOrder.Handle(orderId, CancellationToken.None);

        Assert.Equal(AllocateOrderOutcome.Allocated, first.Outcome);
        Assert.Equal(AllocateOrderOutcome.AlreadyAllocated, second.Outcome);

        var balance = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.SkuA, world.LocA, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(3, balance!.Allocated);

        await using var verifyDb = Db.CreateInventoryContext();
        var reservationCount = await verifyDb.InventoryReservations.CountAsync(r => r.WarehouseId == world.Warehouse);
        Assert.Equal(2, reservationCount);

        var order = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
        Assert.Equal(2, order!.PickTasks.Count);
    }

    // 7 — Location-level pick tasks reservation'dan üretilir.
    [Fact]
    public async Task Pick_tasks_are_generated_at_location_level()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.SkuA, world.LocA, 3);
        await OpenStockAsync(world, world.SkuA, world.LocB, 7);
        await OpenStockAsync(world, world.SkuB, world.LocA, 20);
        await using var bundle = await CreateBundleAsync(world);

        var orderId = await CreateOrderAsync(bundle, world, 10, 4);
        await bundle.AllocateOrder.Handle(orderId, CancellationToken.None);

        var order = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
        var skuATasks = order!.PickTasks.Where(t => t.SkuId == world.SkuA).ToList();
        Assert.Equal(2, skuATasks.Count);
        Assert.Contains(skuATasks, t => t.LocationId == world.LocA && t.RequiredQuantity == 3);
        Assert.Contains(skuATasks, t => t.LocationId == world.LocB && t.RequiredQuantity == 7);
        var skuBTasks = order.PickTasks.Single(t => t.SkuId == world.SkuB);
        Assert.Equal(4, skuBTasks.RequiredQuantity);
    }

    // 8 — Wrong location scan reddedilir.
    [Fact]
    public async Task Wrong_location_scan_is_rejected()
    {
        var (bundle, world, orderId) = await AllocateSingleLineOrderAsync(10);
        await using (bundle)
        {
            var task = (await bundle.GetOrder.Handle(orderId, CancellationToken.None))!.PickTasks.Single();

            await Assert.ThrowsAsync<PickLocationMismatchException>(() => bundle.ConfirmPick.Handle(
                new ConfirmPickCommand(task.Id, world.CodeB, world.BarcodeA, 5),
                CancellationToken.None));

            await Assert.ThrowsAsync<PickLocationMismatchException>(() => bundle.ConfirmPick.Handle(
                new ConfirmPickCommand(task.Id, "NO-SUCH-LOC", world.BarcodeA, 5),
                CancellationToken.None));
        }
    }

    // 9 — Wrong barcode/SKU reddedilir.
    [Fact]
    public async Task Wrong_barcode_is_rejected()
    {
        var (bundle, world, orderId) = await AllocateSingleLineOrderAsync(10);
        await using (bundle)
        {
            var task = (await bundle.GetOrder.Handle(orderId, CancellationToken.None))!.PickTasks.Single();

            await Assert.ThrowsAsync<PickSkuMismatchException>(() => bundle.ConfirmPick.Handle(
                new ConfirmPickCommand(task.Id, world.CodeA, world.BarcodeB, 5),
                CancellationToken.None));

            await Assert.ThrowsAsync<PickSkuMismatchException>(() => bundle.ConfirmPick.Handle(
                new ConfirmPickCommand(task.Id, world.CodeA, "UNKNOWN-BC", 5),
                CancellationToken.None));
        }
    }

    // 10 — Partial pick Completed olmaz.
    [Fact]
    public async Task Partial_pick_does_not_complete_task()
    {
        var (bundle, world, orderId) = await AllocateSingleLineOrderAsync(10);
        await using (bundle)
        {
            var taskId = (await bundle.GetOrder.Handle(orderId, CancellationToken.None))!.PickTasks.Single().Id;

            var result = await bundle.ConfirmPick.Handle(
                new ConfirmPickCommand(taskId, world.CodeA, world.BarcodeA, 3),
                CancellationToken.None);

            Assert.False(result.TaskCompleted);
            Assert.Equal(3, result.PickedQuantity);
            Assert.Equal(7, result.RemainingQuantity);

            var task = await bundle.GetPickTask.Handle(taskId, CancellationToken.None);
            Assert.Equal(PickTaskStatus.InProgress, task!.Status);
            Assert.Equal(3, task.PickedQuantity);

            var order = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
            Assert.Equal(OrderStatus.Picking, order!.Status);
        }
    }

    // 11 — Valid pick tamamlanır.
    [Fact]
    public async Task Valid_pick_completes_task_and_order_goes_picked()
    {
        var (bundle, world, orderId) = await AllocateSingleLineOrderAsync(10);
        await using (bundle)
        {
            var taskId = (await bundle.GetOrder.Handle(orderId, CancellationToken.None))!.PickTasks.Single().Id;

            var result = await bundle.ConfirmPick.Handle(
                new ConfirmPickCommand(taskId, world.CodeA, world.BarcodeA, 10),
                CancellationToken.None);

            Assert.True(result.TaskCompleted);
            Assert.Equal(0, result.RemainingQuantity);

            var task = await bundle.GetPickTask.Handle(taskId, CancellationToken.None);
            Assert.Equal(PickTaskStatus.Completed, task!.Status);

            var order = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
            Assert.Equal(OrderStatus.Picked, order!.Status);
        }
    }

    // 12 + 13 — PickNotFound Accuracy Signal üretir; balance'ı değiştirmez.
    [Fact]
    public async Task Pick_not_found_produces_accuracy_signal_but_does_not_change_stock()
    {
        var (bundle, world, orderId) = await AllocateSingleLineOrderAsync(10);
        await using (bundle)
        {
            var task = (await bundle.GetOrder.Handle(orderId, CancellationToken.None))!.PickTasks.Single();

            var result = await bundle.MarkPickNotFound.Handle(
                new MarkPickNotFoundCommand(task.Id, Guid.NewGuid()),
                CancellationToken.None);

            Assert.True(result.OrderPickException);

            var signals = await bundle.InventoryStore.ListAccuracySignalsAsync(
                world.Warehouse, world.SkuA, world.LocA, null, null, null, 20, CancellationToken.None);
            var signal = Assert.Single(signals, s => s.SignalType == AccuracySignalType.PickNotFound);
            Assert.Equal(task.Id.ToString(), signal.SourceReferenceId.ToString());

            var balance = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.SkuA, world.LocA, InventoryStatus.Available, CancellationToken.None);
            Assert.Equal(10, balance!.Quantity);
            Assert.Equal(10, balance.Allocated);

            var order = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
            Assert.Equal(OrderStatus.PickException, order!.Status);
            Assert.Equal(PickTaskStatus.NotFound, (await bundle.GetPickTask.Handle(task.Id, CancellationToken.None))!.Status);
        }
    }

    // 14 — İki gerçek NotFound risk RED + dynamic cycle count tetikler.
    [Fact]
    public async Task Two_real_not_founds_trigger_red_risk_and_cycle_count_task()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.SkuA, world.LocA, 10);

        // Gerçekçi senaryo: stok 200 gündür fiziksel hareket görmemiş (eski stok).
        await using (var inventoryDb = Db.CreateInventoryContext())
        {
            await inventoryDb.Database.ExecuteSqlRawAsync(
                "UPDATE inventory.inventory_ledger SET occurred_at = now() - interval '200 days' WHERE warehouse_id = {0} AND sku_id = {1}",
                world.Warehouse,
                world.SkuA);
        }

        var order1 = await CreateAllocatedOrderAsync(world, 5);
        var order2 = await CreateAllocatedOrderAsync(world, 5);

        await using var bundle = await CreateBundleAsync(world);
        var task1 = (await bundle.GetOrder.Handle(order1, CancellationToken.None))!.PickTasks.Single();
        var task2 = (await bundle.GetOrder.Handle(order2, CancellationToken.None))!.PickTasks.Single();

        await bundle.MarkPickNotFound.Handle(new MarkPickNotFoundCommand(task1.Id, Guid.NewGuid()), CancellationToken.None);
        await bundle.MarkPickNotFound.Handle(new MarkPickNotFoundCommand(task2.Id, Guid.NewGuid()), CancellationToken.None);

        var analyzer = new InventoryRiskAnalyzer(new RiskPolicyOptions());
        var listRisk = new ListRiskAssessments(bundle.InventoryStore, new FacilityQueryContract(bundle.FacilityDb), analyzer);
        var assessments = await listRisk.Handle(world.Warehouse, world.SkuA, world.LocA, null, 10, CancellationToken.None);
        var assessment = Assert.Single(assessments);
        Assert.True(assessment.RiskScore >= 80, $"Beklenen RED risk; skor: {assessment.RiskScore}");
        Assert.Equal(RiskLevel.Red, assessment.RiskLevel);

        var evaluate = new EvaluateCycleCountCandidates(bundle.InventoryStore, listRisk, analyzer);
        var result = await evaluate.Handle(world.Warehouse, CancellationToken.None);
        Assert.True(result.Created >= 1);

        var queue = await bundle.InventoryStore.GetCycleCountQueueAsync(world.Warehouse, 20, CancellationToken.None);
        var task = Assert.Single(queue, t => t.SkuId == world.SkuA && t.LocationId == world.LocA);
        Assert.Equal(CycleCountReason.RepeatedNotFound, task.Reason);
    }

    // 15 — PickNotFound sonrası order açık exception state'e geçer.
    [Fact]
    public async Task Order_goes_pick_exception_after_not_found()
    {
        var (bundle, world, orderId) = await AllocateSingleLineOrderAsync(10);
        await using (bundle)
        {
            var task = (await bundle.GetOrder.Handle(orderId, CancellationToken.None))!.PickTasks.Single();
            await bundle.MarkPickNotFound.Handle(new MarkPickNotFoundCommand(task.Id, Guid.NewGuid()), CancellationToken.None);

            var order = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
            Assert.Equal(OrderStatus.PickException, order!.Status);
        }
    }

    // 16 — Tüm pick'ler bitmeden pack reddedilir.
    [Fact]
    public async Task Pack_is_rejected_before_all_picks_complete()
    {
        var (bundle, world, orderId) = await AllocateSingleLineOrderAsync(10);
        await using (bundle)
        {
            await Assert.ThrowsAsync<InvalidOrderStateException>(() => bundle.PackOrder.Handle(
                new PackOrderCommand(orderId, Guid.NewGuid()),
                CancellationToken.None));

            var task = (await bundle.GetOrder.Handle(orderId, CancellationToken.None))!.PickTasks.Single();
            await bundle.ConfirmPick.Handle(new ConfirmPickCommand(task.Id, world.CodeA, world.BarcodeA, 10), CancellationToken.None);

            var pack = await bundle.PackOrder.Handle(new PackOrderCommand(orderId, Guid.NewGuid()), CancellationToken.None);
            Assert.Equal(PackOrderOutcome.Packed, pack.Outcome);
        }
    }

    // 17 + 18 — Valid pack başarılı; stok değişmez.
    [Fact]
    public async Task Valid_pack_succeeds_and_does_not_mutate_stock()
    {
        var (bundle, world, orderId) = await AllocateSingleLineOrderAsync(10);
        await using (bundle)
        {
            var task = (await bundle.GetOrder.Handle(orderId, CancellationToken.None))!.PickTasks.Single();
            await bundle.ConfirmPick.Handle(new ConfirmPickCommand(task.Id, world.CodeA, world.BarcodeA, 10), CancellationToken.None);

            var before = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.SkuA, world.LocA, InventoryStatus.Available, CancellationToken.None);

            var pack = await bundle.PackOrder.Handle(new PackOrderCommand(orderId, Guid.NewGuid()), CancellationToken.None);

            Assert.Equal(PackOrderOutcome.Packed, pack.Outcome);
            var order = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
            Assert.Equal(OrderStatus.Packed, order!.Status);
            Assert.NotNull(order.Package);
            Assert.Equal(PackageStatus.Packed, order.Package!.Status);

            var after = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.SkuA, world.LocA, InventoryStatus.Available, CancellationToken.None);
            Assert.Equal(before!.Quantity, after!.Quantity);
            Assert.Equal(before.Allocated, after.Allocated);
        }
    }

    // 19 — Packed olmayan order ship edilemez.
    [Fact]
    public async Task Ship_is_rejected_before_packed()
    {
        var (bundle, world, orderId) = await AllocateSingleLineOrderAsync(10);
        await using (bundle)
        {
            var task = (await bundle.GetOrder.Handle(orderId, CancellationToken.None))!.PickTasks.Single();
            await bundle.ConfirmPick.Handle(new ConfirmPickCommand(task.Id, world.CodeA, world.BarcodeA, 10), CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOrderStateException>(() => bundle.ShipOrder.Handle(
                new ShipOrderCommand(orderId, Guid.NewGuid()),
                CancellationToken.None));
        }
    }

    // 20 + 21 + 22 — Ship ConsumeReservation kullanır; Quantity/Allocated/Ledger doğru.
    [Fact]
    public async Task Ship_consumes_reservation_and_updates_stock_and_ledger()
    {
        var (bundle, world, orderId) = await AllocateSingleLineOrderAsync(10);
        await using (bundle)
        {
            var task = (await bundle.GetOrder.Handle(orderId, CancellationToken.None))!.PickTasks.Single();
            await bundle.ConfirmPick.Handle(new ConfirmPickCommand(task.Id, world.CodeA, world.BarcodeA, 10), CancellationToken.None);
            await bundle.PackOrder.Handle(new PackOrderCommand(orderId, Guid.NewGuid()), CancellationToken.None);

            var order = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
            var reservationId = order!.Lines.Single().ReservationId!.Value;

            var ship = await bundle.ShipOrder.Handle(
                new ShipOrderCommand(orderId, Guid.NewGuid(), "TRACK-1", "UPS"),
                CancellationToken.None);

            Assert.Equal(ShipOrderOutcome.Shipped, ship.Outcome);

            var balance = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.SkuA, world.LocA, InventoryStatus.Available, CancellationToken.None);
            Assert.Equal(0, balance!.Quantity);
            Assert.Equal(0, balance.Allocated);

            var detail = await bundle.InventoryContract.GetReservationAsync(reservationId, CancellationToken.None);
            Assert.Equal("CONSUMED", detail!.Status);

            var ledger = await bundle.InventoryStore.ListLedgerAsync(world.Warehouse, world.SkuA, null, 30, CancellationToken.None);
            Assert.Single(ledger, e => e.EntryType == LedgerEntryType.Reserved);
            var consumed = Assert.Single(ledger, e => e.EntryType == LedgerEntryType.ReservationConsumed);
            Assert.Equal(-10, consumed.QuantityDelta);
            Assert.Equal(-10, consumed.AllocatedDelta);

            var shippedOrder = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
            Assert.Equal(OrderStatus.Shipped, shippedOrder!.Status);
            Assert.NotNull(shippedOrder.ShippedAt);
            Assert.NotNull(shippedOrder.Shipment);
            Assert.Equal("TRACK-1", shippedOrder.Shipment!.TrackingNumber);
        }
    }

    // 23 — Duplicate ship duplicate consume oluşturmaz.
    [Fact]
    public async Task Duplicate_ship_does_not_double_consume()
    {
        var (bundle, world, orderId) = await AllocateSingleLineOrderAsync(10);
        await using (bundle)
        {
            var task = (await bundle.GetOrder.Handle(orderId, CancellationToken.None))!.PickTasks.Single();
            await bundle.ConfirmPick.Handle(new ConfirmPickCommand(task.Id, world.CodeA, world.BarcodeA, 10), CancellationToken.None);
            await bundle.PackOrder.Handle(new PackOrderCommand(orderId, Guid.NewGuid()), CancellationToken.None);

            var first = await bundle.ShipOrder.Handle(new ShipOrderCommand(orderId, Guid.NewGuid()), CancellationToken.None);
            var second = await bundle.ShipOrder.Handle(new ShipOrderCommand(orderId, Guid.NewGuid()), CancellationToken.None);

            Assert.Equal(ShipOrderOutcome.Shipped, first.Outcome);
            Assert.Equal(ShipOrderOutcome.AlreadyShipped, second.Outcome);
            Assert.Equal(first.ShipmentId, second.ShipmentId);

            var balance = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.SkuA, world.LocA, InventoryStatus.Available, CancellationToken.None);
            Assert.Equal(0, balance!.Quantity);

            var ledger = await bundle.InventoryStore.ListLedgerAsync(world.Warehouse, world.SkuA, null, 30, CancellationToken.None);
            Assert.Single(ledger, e => e.EntryType == LedgerEntryType.ReservationConsumed);
        }
    }

    // 24 — Inventory success + crash/retry duplicate consumption yaratmaz.
    [Fact]
    public async Task Crash_after_consume_recovers_without_double_consumption()
    {
        var (bundle, world, orderId) = await AllocateSingleLineOrderAsync(10);
        await using (bundle)
        {
            var task = (await bundle.GetOrder.Handle(orderId, CancellationToken.None))!.PickTasks.Single();
            await bundle.ConfirmPick.Handle(new ConfirmPickCommand(task.Id, world.CodeA, world.BarcodeA, 10), CancellationToken.None);
            await bundle.PackOrder.Handle(new PackOrderCommand(orderId, Guid.NewGuid()), CancellationToken.None);

            var order = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
            var reservationId = order!.Lines.Single().ReservationId!.Value;

            // Crash simülasyonu: consume Inventory'de olur ama Outbound shipment state yazılmaz.
            await bundle.InventoryContract.ConsumeReservationAsync(reservationId, CancellationToken.None);

            var retry = await bundle.ShipOrder.Handle(new ShipOrderCommand(orderId, Guid.NewGuid()), CancellationToken.None);

            Assert.Equal(ShipOrderOutcome.Shipped, retry.Outcome);
            var balance = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.SkuA, world.LocA, InventoryStatus.Available, CancellationToken.None);
            Assert.Equal(0, balance!.Quantity);
            var ledger = await bundle.InventoryStore.ListLedgerAsync(world.Warehouse, world.SkuA, null, 30, CancellationToken.None);
            Assert.Single(ledger, e => e.EntryType == LedgerEntryType.ReservationConsumed);
        }
    }

    // 25 — Cancel allocated order reservation release eder.
    [Fact]
    public async Task Cancel_allocated_order_releases_reservations()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.SkuA, world.LocA, 10);
        await OpenStockAsync(world, world.SkuB, world.LocA, 20);
        await using var bundle = await CreateBundleAsync(world);

        var orderId = await CreateOrderAsync(bundle, world, 3, 4);
        await bundle.AllocateOrder.Handle(orderId, CancellationToken.None);
        var order = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
        var reservationIds = order!.Lines.Select(l => l.ReservationId!.Value).ToList();

        await bundle.CancelOrder.Handle(orderId, CancellationToken.None);

        var cancelled = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
        Assert.Equal(OrderStatus.Cancelled, cancelled!.Status);
        Assert.All(cancelled.PickTasks, t => Assert.Equal(PickTaskStatus.Cancelled, t.Status));

        foreach (var reservationId in reservationIds)
        {
            var detail = await bundle.InventoryContract.GetReservationAsync(reservationId, CancellationToken.None);
            Assert.Equal("RELEASED", detail!.Status);
        }

        var balance = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.SkuA, world.LocA, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(0, balance!.Allocated);
    }

    // 26 — Shipped order normal cancel edilemez.
    [Fact]
    public async Task Shipped_order_cannot_be_cancelled()
    {
        var (bundle, world, orderId) = await AllocateSingleLineOrderAsync(10);
        await using (bundle)
        {
            var task = (await bundle.GetOrder.Handle(orderId, CancellationToken.None))!.PickTasks.Single();
            await bundle.ConfirmPick.Handle(new ConfirmPickCommand(task.Id, world.CodeA, world.BarcodeA, 10), CancellationToken.None);
            await bundle.PackOrder.Handle(new PackOrderCommand(orderId, Guid.NewGuid()), CancellationToken.None);
            await bundle.ShipOrder.Handle(new ShipOrderCommand(orderId, Guid.NewGuid()), CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOrderStateException>(() => bundle.CancelOrder.Handle(orderId, CancellationToken.None));

            var order = await bundle.GetOrder.Handle(orderId, CancellationToken.None);
            Assert.Equal(OrderStatus.Shipped, order!.Status);
        }
    }

    // 27 + 29 — Outbound Inventory tablosuna yazmaz; cross-module FK yok.
    [Fact]
    public async Task Outbound_tables_have_no_cross_module_foreign_keys()
    {
        await using var db = Db.CreateOutboundContext();
        var rows = await db.Database.SqlQueryRaw<FkRow>(
                """
                SELECT tc.table_name AS "table_name", ccu.table_schema AS "fk_schema", ccu.table_name AS "fk_table"
                FROM information_schema.table_constraints tc
                JOIN information_schema.constraint_column_usage ccu ON tc.constraint_name = ccu.constraint_name
                WHERE tc.constraint_type = 'FOREIGN KEY'
                  AND tc.table_schema = 'outbound'
                """)
            .ToListAsync();

        Assert.All(rows, r => Assert.Equal("outbound", r.FkSchema));
    }

    // 28 — outbound.allocations tablosu oluşturulmamıştır.
    [Fact]
    public async Task Outbound_has_no_allocations_table()
    {
        await using var db = Db.CreateOutboundContext();
        var exists = await db.Database.SqlQueryRaw<bool>(
                """
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = 'outbound' AND table_name = 'allocations'
                ) AS "Value"
                """)
            .SingleAsync();
        Assert.False(exists);
    }

    // 30 — Gerçek PostgreSQL concurrency: concurrent allocate tek reservation seti üretir.
    [Fact]
    public async Task Concurrent_allocation_produces_single_reservation_set()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.SkuA, world.LocA, 10);
        await OpenStockAsync(world, world.SkuB, world.LocA, 20);

        await using var setupBundle = await CreateBundleAsync(world);
        var orderId = await CreateOrderAsync(setupBundle, world, 3, 4);

        var outcomes = await Task.WhenAll(
            RunAllocateAsync(world, orderId),
            RunAllocateAsync(world, orderId));

        Assert.Equal(1, outcomes.Count(o => o == AllocateOrderOutcome.Allocated));
        Assert.Equal(1, outcomes.Count(o => o == AllocateOrderOutcome.AlreadyAllocated));

        await using var verifyDb = Db.CreateInventoryContext();
        var reservationCount = await verifyDb.InventoryReservations.CountAsync(r => r.WarehouseId == world.Warehouse);
        Assert.Equal(2, reservationCount);

        var store = new InventoryStore(verifyDb);
        var balance = await store.GetBalanceAsync(world.Warehouse, world.SkuA, world.LocA, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(3, balance!.Allocated);
    }

    private static async Task<AllocateOrderOutcome> RunAllocateAsync(World world, Guid orderId)
    {
        await using var bundle = await CreateBundleAsync(world);
        var result = await bundle.AllocateOrder.Handle(orderId, CancellationToken.None);
        return result.Outcome;
    }

    private static async Task<Guid> CreateAllocatedOrderAsync(World world, int quantity)
    {
        await using var bundle = await CreateBundleAsync(world);
        var create = await bundle.CreateOrder.Handle(
            new CreateFulfillmentOrderCommand(
                Guid.NewGuid(),
                null,
                world.Warehouse,
                null,
                [new CreateFulfillmentOrderLineInput(world.SkuA, quantity)]),
            CancellationToken.None);
        await bundle.AllocateOrder.Handle(create.OrderId, CancellationToken.None);
        return create.OrderId;
    }

    private static async Task<(Bundle Bundle, World World, Guid OrderId)> AllocateSingleLineOrderAsync(int quantity)
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.SkuA, world.LocA, quantity);
        var bundle = await CreateBundleAsync(world);
        var create = await bundle.CreateOrder.Handle(
            new CreateFulfillmentOrderCommand(
                Guid.NewGuid(),
                null,
                world.Warehouse,
                null,
                [new CreateFulfillmentOrderLineInput(world.SkuA, quantity)]),
            CancellationToken.None);
        await bundle.AllocateOrder.Handle(create.OrderId, CancellationToken.None);
        return (bundle, world, create.OrderId);
    }

    private sealed class FkRow
    {
        public string TableName { get; set; } = string.Empty;

        public string FkSchema { get; set; } = string.Empty;

        public string FkTable { get; set; } = string.Empty;
    }

    private sealed record World(
        Guid SkuA,
        string BarcodeA,
        Guid SkuB,
        string BarcodeB,
        Guid Warehouse,
        Guid LocA,
        string CodeA,
        Guid LocB,
        string CodeB);

    private sealed class Bundle : IAsyncDisposable
    {
        private readonly OutboundDbContext _outboundDb;
        private readonly InventoryDbContext _inventoryDb;
        private readonly FacilityDbContext _facilityDb;
        private readonly MasterDataDbContext _masterDb;

        public Bundle(
            OutboundStore outboundStore,
            InventoryStore inventoryStore,
            IInventoryContract inventoryContract,
            CreateFulfillmentOrder createOrder,
            AllocateOrder allocateOrder,
            StartPick startPick,
            ConfirmPick confirmPick,
            MarkPickNotFound markPickNotFound,
            PackOrder packOrder,
            ShipOrder shipOrder,
            CancelOrder cancelOrder,
            GetOrder getOrder,
            ListOrders listOrders,
            GetPickTask getPickTask,
            ListPickTasks listPickTasks,
            OutboundDbContext outboundDb,
            InventoryDbContext inventoryDb,
            FacilityDbContext facilityDb,
            MasterDataDbContext masterDb)
        {
            OutboundStore = outboundStore;
            InventoryStore = inventoryStore;
            InventoryContract = inventoryContract;
            CreateOrder = createOrder;
            AllocateOrder = allocateOrder;
            StartPick = startPick;
            ConfirmPick = confirmPick;
            MarkPickNotFound = markPickNotFound;
            PackOrder = packOrder;
            ShipOrder = shipOrder;
            CancelOrder = cancelOrder;
            GetOrder = getOrder;
            ListOrders = listOrders;
            GetPickTask = getPickTask;
            ListPickTasks = listPickTasks;
            _outboundDb = outboundDb;
            _inventoryDb = inventoryDb;
            _facilityDb = facilityDb;
            _masterDb = masterDb;
        }

        public OutboundStore OutboundStore { get; }

        public InventoryStore InventoryStore { get; }

        public IInventoryContract InventoryContract { get; }

        public CreateFulfillmentOrder CreateOrder { get; }

        public AllocateOrder AllocateOrder { get; }

        public StartPick StartPick { get; }

        public ConfirmPick ConfirmPick { get; }

        public MarkPickNotFound MarkPickNotFound { get; }

        public PackOrder PackOrder { get; }

        public ShipOrder ShipOrder { get; }

        public CancelOrder CancelOrder { get; }

        public GetOrder GetOrder { get; }

        public ListOrders ListOrders { get; }

        public GetPickTask GetPickTask { get; }

        public ListPickTasks ListPickTasks { get; }

        public FacilityDbContext FacilityDb => _facilityDb;

        public async ValueTask DisposeAsync()
        {
            await _outboundDb.DisposeAsync();
            await _inventoryDb.DisposeAsync();
            await _facilityDb.DisposeAsync();
            await _masterDb.DisposeAsync();
        }
    }
}
