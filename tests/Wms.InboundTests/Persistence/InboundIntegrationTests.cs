using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wms.Modules.Facility.Domain;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Inbound.Application;
using Wms.Modules.Inbound.Domain;
using Wms.Modules.Inbound.Infrastructure.Persistence;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Application.Accuracy;
using Wms.Modules.Inventory.Application.Accuracy.Scanning;
using Wms.Modules.Inventory.Contracts;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Infrastructure;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.MasterData.Infrastructure.Persistence;
using Xunit;

namespace Wms.InboundTests.Persistence;

public sealed class InboundIntegrationTests
{
    private static async Task<World> CreateWorldAsync()
    {
        var (sku, barcode) = await Db.CreateSkuWithBarcodeAsync();
        var warehouse = await Db.CreateWarehouseAsync();
        var (receiving, receivingCode) = await Db.CreateLocationAsync(warehouse, LocationType.Receiving, holdsInventory: true);
        var (storage, storageCode) = await Db.CreateLocationAsync(warehouse, LocationType.Storage, holdsInventory: true);
        return new World(sku, barcode, warehouse, receiving, receivingCode, storage, storageCode);
    }

    private static async Task<Bundle> CreateBundleAsync(World world, bool allowOverReceipt = false)
    {
        var inventoryDb = Db.CreateInventoryContext();
        var facilityDb = Db.CreateFacilityContext();
        var masterDb = Db.CreateMasterDataContext();
        var inboundDb = Db.CreateInboundContext();

        var masterContract = new MasterDataQueryContract(masterDb);
        var facilityContract = new FacilityQueryContract(facilityDb);
        var inventoryStore = new InventoryStore(inventoryDb);
        var inboundStore = new InboundStore(inboundDb);

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

        var options = Options.Create(new InboundOptions { AllowOverReceipt = allowOverReceipt });

        return new Bundle(
            inventoryStore,
            new CreateReceipt(inboundStore, masterContract, facilityContract),
            new ReceiveItems(inboundStore, masterContract, facilityContract, inventoryContract, options),
            new StartPutaway(inboundStore),
            new CompletePutaway(inboundStore, masterContract, facilityContract, inventoryContract),
            new CancelReceipt(inboundStore),
            new GetReceipt(inboundStore),
            new ListReceipts(inboundStore),
            new GetPutawayTask(inboundStore),
            new ListPutawayTasks(inboundStore),
            inventoryContract,
            inboundDb,
            inventoryDb,
            facilityDb,
            masterDb);
    }

    private static async Task<CreateReceiptResult> CreateReceiptAsync(Bundle bundle, World world, int expected = 10, string? externalRef = null)
    {
        return await bundle.CreateReceipt.Handle(
            new CreateReceiptCommand(
                Guid.NewGuid(),
                null,
                world.Warehouse,
                externalRef,
                "ASN",
                [new CreateReceiptLineInput(world.Sku, expected)]),
            CancellationToken.None);
    }

    // 1 — Expected receipt oluşturulur.
    [Fact]
    public async Task Expected_receipt_is_created_open_with_lines()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);

        var result = await CreateReceiptAsync(bundle, world, expected: 10);

        Assert.Equal(CreateReceiptOutcome.Created, result.Outcome);
        Assert.StartsWith("INB-", result.ReceiptNumber);

        var receipt = await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None);
        Assert.NotNull(receipt);
        Assert.Equal(ReceiptStatus.Open, receipt!.Status);
        var line = Assert.Single(receipt.Lines);
        Assert.Equal(10, line.ExpectedQuantity);
        Assert.Equal(0, line.ReceivedQuantity);
        Assert.Null(line.Disposition);
        Assert.Equal("ASN", receipt.SourceType);
    }

    // 2 — Unknown/inactive SKU reddedilir.
    [Fact]
    public async Task Unknown_or_inactive_sku_is_rejected()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);

        await Assert.ThrowsAsync<InvalidReceiptStateException>(() => bundle.CreateReceipt.Handle(
            new CreateReceiptCommand(Guid.NewGuid(), null, world.Warehouse, null, null,
                [new CreateReceiptLineInput(Guid.NewGuid(), 5)]),
            CancellationToken.None));

        await using (var masterDb = Db.CreateMasterDataContext())
        {
            var sku = await masterDb.Skus.FirstAsync(s => s.Id == world.Sku);
            sku.Deactivate();
            await masterDb.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidReceiptStateException>(() => bundle.CreateReceipt.Handle(
            new CreateReceiptCommand(Guid.NewGuid(), null, world.Warehouse, null, null,
                [new CreateReceiptLineInput(world.Sku, 5)]),
            CancellationToken.None));
    }

    // 3 — Invalid warehouse reddedilir.
    [Fact]
    public async Task Invalid_warehouse_is_rejected()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);

        await Assert.ThrowsAsync<InvalidReceiptStateException>(() => bundle.CreateReceipt.Handle(
            new CreateReceiptCommand(Guid.NewGuid(), null, Guid.NewGuid(), null, null,
                [new CreateReceiptLineInput(world.Sku, 5)]),
            CancellationToken.None));
    }

    // 4 — Invalid receiving location reddedilir.
    [Fact]
    public async Task Invalid_receiving_location_is_rejected()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 10);
        var receiptId = result.ReceiptId;
        var lineId = (await bundle.GetReceipt.Handle(receiptId, CancellationToken.None))!.Lines.Single().Id;

        await Assert.ThrowsAsync<InvalidReceivingLocationException>(() => bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), receiptId, lineId, 5, Guid.NewGuid(), ReceivingStockStatus.Available),
            CancellationToken.None));

        await Assert.ThrowsAsync<InvalidReceivingLocationException>(() => bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), receiptId, lineId, 5, world.Storage, ReceivingStockStatus.Available),
            CancellationToken.None));

        var (dock, _) = await Db.CreateLocationAsync(world.Warehouse, LocationType.Dock, holdsInventory: false);
        await Assert.ThrowsAsync<InvalidReceivingLocationException>(() => bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), receiptId, lineId, 5, dock, ReceivingStockStatus.Available),
            CancellationToken.None));
    }

    // 5 — Partial receipt desteklenir (iki teslimat).
    [Fact]
    public async Task Partial_receipt_is_supported_across_multiple_deliveries()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 10);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;

        var first = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 6, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        var afterFirst = await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None);
        Assert.Equal(ReceiptStatus.PartiallyReceived, afterFirst!.Status);
        Assert.Equal(6, afterFirst.Lines.Single().ReceivedQuantity);
        Assert.Equal(ReceivingDisposition.Short, afterFirst.Lines.Single().Disposition);
        Assert.Equal(6, (await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Receiving, InventoryStatus.Available, CancellationToken.None))!.Quantity);

        var second = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 4, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        var afterSecond = await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None);
        Assert.Equal(ReceiptStatus.Received, afterSecond!.Status);
        Assert.Equal(10, afterSecond.Lines.Single().ReceivedQuantity);
        Assert.Equal(ReceivingDisposition.Matched, afterSecond.Lines.Single().Disposition);
        Assert.Equal(10, (await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Receiving, InventoryStatus.Available, CancellationToken.None))!.Quantity);
        Assert.True(first.ReceiveRecordId != Guid.Empty);
        Assert.True(second.ReceiveRecordId != Guid.Empty);
    }

    // 6 — Exact receipt MATCHED.
    [Fact]
    public async Task Exact_receipt_is_matched()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 10);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;

        var receive = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 10, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        Assert.Equal(ReceivingDisposition.Matched, receive.Disposition);
        var receipt = await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None);
        Assert.Equal(ReceiptStatus.Received, receipt!.Status);
    }

    // 7 — Short receipt doğru hesaplanır.
    [Fact]
    public async Task Short_receipt_is_computed_correctly()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 10);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;

        var receive = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 8, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        Assert.Equal(ReceivingDisposition.Short, receive.Disposition);
        Assert.Equal(8, receive.LineReceivedQuantity);
        var receipt = await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None);
        Assert.Equal(ReceiptStatus.PartiallyReceived, receipt!.Status);
        Assert.Equal(ReceivingDisposition.Short, receipt.Lines.Single().Disposition);
    }

    // 8 — Over receipt policy uygulanır.
    [Fact]
    public async Task Over_receipt_policy_is_enforced()
    {
        var world = await CreateWorldAsync();
        await using var strict = await CreateBundleAsync(world, allowOverReceipt: false);
        var result = await CreateReceiptAsync(strict, world, expected: 10);
        var lineId = (await strict.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;

        await Assert.ThrowsAsync<OverReceiptNotAllowedException>(() => strict.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 12, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None));

        await using var permissive = await CreateBundleAsync(world, allowOverReceipt: true);
        var receipt2 = await CreateReceiptAsync(permissive, world, expected: 10);
        var lineId2 = (await permissive.GetReceipt.Handle(receipt2.ReceiptId, CancellationToken.None))!.Lines.Single().Id;

        var receive = await permissive.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), receipt2.ReceiptId, lineId2, 12, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        Assert.Equal(ReceivingDisposition.Over, receive.Disposition);
        Assert.Equal(12, (await permissive.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Receiving, InventoryStatus.Available, CancellationToken.None))!.Quantity);
    }

    // 9 — AVAILABLE receipt doğru partition'a girer.
    [Fact]
    public async Task Available_receipt_enters_available_partition()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 10);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;

        await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 10, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        var balance = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Receiving, InventoryStatus.Available, CancellationToken.None);
        Assert.NotNull(balance);
        Assert.Equal(10, balance!.Quantity);
        Assert.Equal(10, balance.Available);
    }

    // 10 — DAMAGED receipt DAMAGED balance'a girer.
    [Fact]
    public async Task Damaged_receipt_enters_damaged_partition()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 10);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;

        await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 8, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);
        await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 2, world.Receiving, ReceivingStockStatus.Damaged),
            CancellationToken.None);

        var damaged = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Receiving, InventoryStatus.Damaged, CancellationToken.None);
        Assert.NotNull(damaged);
        Assert.Equal(2, damaged!.Quantity);
        Assert.Equal(8, (await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Receiving, InventoryStatus.Available, CancellationToken.None))!.Quantity);
    }

    // 11 — QUARANTINE receipt allocation'a açık olmaz.
    [Fact]
    public async Task Quarantine_receipt_is_not_open_for_allocation()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 10);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;

        await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 10, world.Receiving, ReceivingStockStatus.Quarantine),
            CancellationToken.None);

        var quarantine = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Receiving, InventoryStatus.Quarantine, CancellationToken.None);
        Assert.Equal(10, quarantine!.Quantity);
        Assert.Equal(0, quarantine.Allocated);

        var summary = await new GetWarehouseSkuSummary(bundle.InventoryStore).Handle(world.Warehouse, world.Sku, CancellationToken.None);
        Assert.Equal(0, summary.Available);
        Assert.Equal(10, summary.OnHand);

        await Assert.ThrowsAsync<InsufficientInventoryException>(() => bundle.Reserve.Handle(
            new ReserveCommand(Guid.NewGuid(), world.Sku, world.Warehouse, 3, "test"),
            CancellationToken.None));
    }

    // 12 — Receive operation Inventory Balance + Ledger oluşturur.
    [Fact]
    public async Task Receive_operation_writes_balance_and_received_ledger_entry()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 10);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;
        var requestId = Guid.NewGuid();

        await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(requestId, result.ReceiptId, lineId, 10, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        var ledger = await bundle.InventoryStore.ListLedgerAsync(world.Warehouse, world.Sku, null, 20, CancellationToken.None);
        var received = Assert.Single(ledger, e => e.EntryType == LedgerEntryType.Received);
        Assert.Equal(10, received.QuantityDelta);
        Assert.Equal(requestId, received.RequestId);
        Assert.Equal("INBOUND_RECEIPT", received.ReferenceType);
        Assert.Equal(result.ReceiptId, received.ReferenceId);
        Assert.Equal(world.Receiving, received.LocationId);
    }

    // 13 — Duplicate receive RequestId stoğu iki kez artırmaz.
    [Fact]
    public async Task Duplicate_receive_request_id_does_not_double_stock()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 20);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;
        var requestId = Guid.NewGuid();

        var first = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(requestId, result.ReceiptId, lineId, 10, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);
        var second = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(requestId, result.ReceiptId, lineId, 10, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        Assert.Equal(ReceiveItemsOutcome.Received, first.Outcome);
        Assert.Equal(ReceiveItemsOutcome.AlreadyRecorded, second.Outcome);
        Assert.Equal(first.ReceiveRecordId, second.ReceiveRecordId);

        var balance = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Receiving, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(10, balance!.Quantity);

        var receipt = await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None);
        Assert.Single(receipt!.ReceiveRecords);
    }

    // 14 — Inventory success sonrası Inbound crash/retry duplicate stock yaratmaz.
    [Fact]
    public async Task Crash_after_inventory_success_recovers_without_duplicate_stock()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 10);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;
        var requestId = Guid.NewGuid();

        // Crash simülasyonu: Inventory receive doğrudan yapılır (Inbound kaydı YOK).
        var inventoryResult = await bundle.InventoryContract.ReceiveInventoryAsync(
            new Wms.Modules.Inventory.Contracts.ReceiveInventoryCommand(requestId, world.Sku, world.Warehouse, world.Receiving, "AVAILABLE", 10, "INBOUND_RECEIPT", result.ReceiptId),
            CancellationToken.None);
        Assert.Equal(Wms.Modules.Inventory.Contracts.ReceiveInventoryOutcome.Recorded, inventoryResult.Outcome);

        // Retry: aynı RequestId ile Inbound receive çalışır.
        var retry = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(requestId, result.ReceiptId, lineId, 10, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        Assert.Equal(ReceiveItemsOutcome.Received, retry.Outcome);
        Assert.Equal(10, (await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Receiving, InventoryStatus.Available, CancellationToken.None))!.Quantity);

        var receipt = await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None);
        Assert.Single(receipt!.ReceiveRecords);
        Assert.Equal(ReceiptStatus.Received, receipt.Status);

        var again = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(requestId, result.ReceiptId, lineId, 10, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);
        Assert.Equal(ReceiveItemsOutcome.AlreadyRecorded, again.Outcome);
        Assert.Equal(10, (await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Receiving, InventoryStatus.Available, CancellationToken.None))!.Quantity);
    }

    // 15 — Receive history korunur.
    [Fact]
    public async Task Receive_history_is_preserved()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 10);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;

        await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 6, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);
        await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 4, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        var receipt = await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None);
        Assert.Equal(2, receipt!.ReceiveRecords.Count);
        Assert.Contains(receipt.ReceiveRecords, r => r.Quantity == 6);
        Assert.Contains(receipt.ReceiveRecords, r => r.Quantity == 4);
        Assert.All(receipt.ReceiveRecords, r => Assert.Equal(world.Receiving, r.ReceivingLocationId));
        Assert.All(receipt.ReceiveRecords, r => Assert.Equal("AVAILABLE", r.InventoryStatus));
        Assert.All(receipt.ReceiveRecords, r => Assert.True(r.InventoryOperationId != Guid.Empty));
    }

    // 16 — Putaway task doğru quantity için oluşur.
    [Fact]
    public async Task Putaway_task_is_created_for_received_quantity()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 10);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;

        var receive = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 10, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        var task = await bundle.GetPutawayTask.Handle(receive.PutawayTaskId, CancellationToken.None);
        Assert.NotNull(task);
        Assert.Equal(10, task!.Quantity);
        Assert.Equal(world.Receiving, task.SourceLocationId);
        Assert.Equal(PutawayTaskStatus.Pending, task.Status);
        Assert.Equal(result.ReceiptId, task.ReceiptId);
        Assert.Equal(receive.ReceiveRecordId, task.ReceiveRecordId);
    }

    // 17 — Duplicate/concurrent receive putaway quantity aşımı oluşturmaz.
    [Fact]
    public async Task Duplicate_receive_does_not_create_second_putaway_task()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 20);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;
        var requestId = Guid.NewGuid();

        await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(requestId, result.ReceiptId, lineId, 10, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);
        await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(requestId, result.ReceiptId, lineId, 10, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        var tasks = await bundle.ListPutawayTasks.Handle(world.Warehouse, null, 20, CancellationToken.None);
        var matching = tasks.Where(t => t.ReceiptId == result.ReceiptId).ToList();
        var task = Assert.Single(matching);
        Assert.Equal(10, task.Quantity);

        var receipt = await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None);
        Assert.Equal(10, receipt!.Lines.Single().ReceivedQuantity);
    }

    // 18 — Wrong source scan reddedilir.
    [Fact]
    public async Task Wrong_source_scan_is_rejected()
    {
        var (world, taskId) = await ReceiveAndGetTaskAsync();
        await using var bundle = await CreateBundleAsync(world);
        var task = await bundle.GetPutawayTask.Handle(taskId, CancellationToken.None);
        var (otherReceiving, otherCode) = await Db.CreateLocationAsync(world.Warehouse, LocationType.Receiving, holdsInventory: true);
        Assert.True(otherReceiving != Guid.Empty);

        await Assert.ThrowsAsync<PutawaySourceMismatchException>(() => bundle.CompletePutaway.Handle(
            new CompletePutawayCommand(taskId, Guid.NewGuid(), otherCode, world.Barcode, world.StorageCode, task!.Quantity),
            CancellationToken.None));
    }

    // 19 — Wrong SKU scan reddedilir.
    [Fact]
    public async Task Wrong_sku_scan_is_rejected()
    {
        var (world, taskId) = await ReceiveAndGetTaskAsync();
        await using var bundle = await CreateBundleAsync(world);
        var task = await bundle.GetPutawayTask.Handle(taskId, CancellationToken.None);
        var (otherSku, otherBarcode) = await Db.CreateSkuWithBarcodeAsync();

        await Assert.ThrowsAsync<PutawaySkuMismatchException>(() => bundle.CompletePutaway.Handle(
            new CompletePutawayCommand(taskId, Guid.NewGuid(), world.ReceivingCode, otherBarcode, world.StorageCode, task!.Quantity),
            CancellationToken.None));
        Assert.True(otherSku != world.Sku);
    }

    // 20 — Wrong warehouse destination reddedilir.
    [Fact]
    public async Task Wrong_warehouse_destination_is_rejected()
    {
        var (world, taskId) = await ReceiveAndGetTaskAsync();
        await using var bundle = await CreateBundleAsync(world);
        var task = await bundle.GetPutawayTask.Handle(taskId, CancellationToken.None);
        var otherWarehouse = await Db.CreateWarehouseAsync();
        var (foreignLocation, foreignCode) = await Db.CreateLocationAsync(otherWarehouse, LocationType.Storage, holdsInventory: true);
        Assert.True(foreignLocation != Guid.Empty);

        var result = await bundle.CompletePutaway.Handle(
            new CompletePutawayCommand(taskId, Guid.NewGuid(), world.ReceivingCode, world.Barcode, foreignCode, task!.Quantity),
            CancellationToken.None);

        Assert.Equal(PutawayCompletionStatus.Rejected, result.Status);
        Assert.Equal("WrongWarehouse", result.RejectionCode);
    }

    // 21 — Putaway existing scanned relocation mekanizmasını kullanır.
    [Fact]
    public async Task Putaway_reuses_scanned_relocation_and_writes_scan_evidence()
    {
        var (world, taskId) = await ReceiveAndGetTaskAsync(quantity: 10);
        await using var bundle = await CreateBundleAsync(world);
        var task = await bundle.GetPutawayTask.Handle(taskId, CancellationToken.None);

        var result = await bundle.CompletePutaway.Handle(
            new CompletePutawayCommand(taskId, Guid.NewGuid(), world.ReceivingCode, world.Barcode, world.StorageCode, task!.Quantity, "RF-01", "op-1"),
            CancellationToken.None);

        Assert.Equal(PutawayCompletionStatus.Completed, result.Status);
        Assert.NotNull(result.MovementId);

        var evidence = await bundle.InventoryStore.GetScanEvidenceByMovementIdAsync(result.MovementId!.Value, CancellationToken.None);
        Assert.NotNull(evidence);
        Assert.Equal(world.ReceivingCode, evidence!.SourceScanValue);
        Assert.Equal(world.Barcode, evidence.SkuScanValue);
        Assert.Equal(world.StorageCode, evidence.DestinationScanValue);

        var ledger = await bundle.InventoryStore.ListLedgerAsync(world.Warehouse, world.Sku, null, 20, CancellationToken.None);
        Assert.Single(ledger, e => e.EntryType == LedgerEntryType.RelocatedOut);
        Assert.Single(ledger, e => e.EntryType == LedgerEntryType.RelocatedIn);
    }

    // 22 — Putaway tamamlanınca source→destination stock doğru hareket eder.
    [Fact]
    public async Task Putaway_moves_stock_from_source_to_destination()
    {
        var (world, taskId) = await ReceiveAndGetTaskAsync(quantity: 10);
        await using var bundle = await CreateBundleAsync(world);
        var task = await bundle.GetPutawayTask.Handle(taskId, CancellationToken.None);

        var result = await bundle.CompletePutaway.Handle(
            new CompletePutawayCommand(taskId, Guid.NewGuid(), world.ReceivingCode, world.Barcode, world.StorageCode, task!.Quantity),
            CancellationToken.None);

        Assert.Equal(PutawayCompletionStatus.Completed, result.Status);
        var source = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Receiving, InventoryStatus.Available, CancellationToken.None);
        var destination = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Storage, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(0, source!.Quantity);
        Assert.Equal(10, destination!.Quantity);
    }

    // 23 — Putaway retry duplicate movement oluşturmaz.
    [Fact]
    public async Task Putaway_retry_does_not_create_duplicate_movement()
    {
        var (world, taskId) = await ReceiveAndGetTaskAsync(quantity: 10);
        await using var bundle = await CreateBundleAsync(world);
        var task = await bundle.GetPutawayTask.Handle(taskId, CancellationToken.None);
        var requestId = Guid.NewGuid();

        var first = await bundle.CompletePutaway.Handle(
            new CompletePutawayCommand(taskId, requestId, world.ReceivingCode, world.Barcode, world.StorageCode, task!.Quantity),
            CancellationToken.None);
        var second = await bundle.CompletePutaway.Handle(
            new CompletePutawayCommand(taskId, requestId, world.ReceivingCode, world.Barcode, world.StorageCode, task.Quantity),
            CancellationToken.None);

        Assert.Equal(PutawayCompletionStatus.Completed, first.Status);
        Assert.Equal(PutawayCompletionStatus.AlreadyCompleted, second.Status);
        Assert.Equal(first.MovementId, second.MovementId);

        var movements = await bundle.InventoryStore.ListMovementsAsync(world.Warehouse, world.Sku, 20, CancellationToken.None);
        Assert.Single(movements);

        var destination = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Storage, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(10, destination!.Quantity);
    }

    // 24 — Pending putaway varken receipt COMPLETED olmaz.
    [Fact]
    public async Task Receipt_is_not_completed_while_putaway_pending()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 20);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;

        await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 12, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);
        await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 8, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        var receipt = await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None);
        Assert.Equal(ReceiptStatus.Received, receipt!.Status);
        Assert.NotEqual(ReceiptStatus.Completed, receipt.Status);

        var tasks = await bundle.ListPutawayTasks.Handle(world.Warehouse, null, 20, CancellationToken.None);
        Assert.Equal(2, tasks.Count(t => t.ReceiptId == result.ReceiptId && t.Status == PutawayTaskStatus.Pending));
    }

    // 25 — Completed putaway sonrası receipt doğru tamamlanır.
    [Fact]
    public async Task Receipt_completes_after_all_putaway_tasks_complete()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 20);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;

        var receive1 = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 12, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);
        var receive2 = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 8, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        await bundle.StartPutaway.Handle(receive1.PutawayTaskId, CancellationToken.None);
        var receiptMid = await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None);
        Assert.Equal(ReceiptStatus.PutawayInProgress, receiptMid!.Status);

        await bundle.CompletePutaway.Handle(
            new CompletePutawayCommand(receive1.PutawayTaskId, Guid.NewGuid(), world.ReceivingCode, world.Barcode, world.StorageCode, 12),
            CancellationToken.None);

        var receiptAfterFirst = await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None);
        Assert.Equal(ReceiptStatus.PutawayInProgress, receiptAfterFirst!.Status);

        await bundle.CompletePutaway.Handle(
            new CompletePutawayCommand(receive2.PutawayTaskId, Guid.NewGuid(), world.ReceivingCode, world.Barcode, world.StorageCode, 8),
            CancellationToken.None);

        var receiptFinal = await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None);
        Assert.Equal(ReceiptStatus.Completed, receiptFinal!.Status);
        Assert.NotNull(receiptFinal.CompletedAt);
    }

    // 26 — Physical receive sonrası receipt cancellation stoğu sessizce silmez.
    [Fact]
    public async Task Cancellation_after_physical_receive_is_rejected_and_stock_stays()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 10);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;

        await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, 5, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidReceiptStateException>(() => bundle.CancelReceipt.Handle(result.ReceiptId, CancellationToken.None));

        Assert.Equal(5, (await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Receiving, InventoryStatus.Available, CancellationToken.None))!.Quantity);
        var receipt = await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None);
        Assert.Equal(ReceiptStatus.PartiallyReceived, receipt!.Status);
    }

    // 26b — Receive yapılmamış receipt iptal edilebilir.
    [Fact]
    public async Task Unreceived_receipt_can_be_cancelled()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 10);

        var receipt = await bundle.CancelReceipt.Handle(result.ReceiptId, CancellationToken.None);

        Assert.Equal(ReceiptStatus.Cancelled, receipt.Status);
        Assert.NotNull(receipt.CancelledAt);
    }

    // 27 — Cross-module FK yok.
    [Fact]
    public async Task Inbound_tables_have_no_cross_module_foreign_keys()
    {
        await using var db = Db.CreateInboundContext();
        var rows = await db.Database.SqlQueryRaw<FkRow>(
                """
                SELECT tc.table_name AS "table_name", ccu.table_schema AS "fk_schema", ccu.table_name AS "fk_table"
                FROM information_schema.table_constraints tc
                JOIN information_schema.constraint_column_usage ccu ON tc.constraint_name = ccu.constraint_name
                WHERE tc.constraint_type = 'FOREIGN KEY'
                  AND tc.table_schema = 'inbound'
                """)
            .ToListAsync();

        Assert.All(rows, r => Assert.Equal("inbound", r.FkSchema));
    }

    // 28 — Gerçek PostgreSQL integration/concurrency: parallel duplicate receive tek record üretir.
    [Fact]
    public async Task Concurrent_duplicate_receives_produce_single_record_on_postgres()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: 30);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;
        var requestId = Guid.NewGuid();

        var outcomes = await Task.WhenAll(
            RunReceiveAsync(world, result.ReceiptId, lineId, requestId),
            RunReceiveAsync(world, result.ReceiptId, lineId, requestId));

        Assert.Equal(1, outcomes.Count(o => o == ReceiveItemsOutcome.Received));
        Assert.Equal(1, outcomes.Count(o => o == ReceiveItemsOutcome.AlreadyRecorded));

        await using var verifyDb = Db.CreateInboundContext();
        var records = await verifyDb.ReceiptLineReceiveRecords
            .Where(r => r.RequestId == requestId)
            .ToListAsync();
        Assert.Single(records);
        var tasks = await verifyDb.PutawayTasks.Where(t => t.ReceiveRecordId == records[0].Id).ToListAsync();
        Assert.Single(tasks);

        await using var inventoryDb = Db.CreateInventoryContext();
        var verifyStore = new InventoryStore(inventoryDb);
        var balance = await verifyStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Receiving, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(10, balance!.Quantity);
    }

    private static async Task<ReceiveItemsOutcome> RunReceiveAsync(World world, Guid receiptId, Guid lineId, Guid requestId)
    {
        await using var bundle = await CreateBundleAsync(world);
        var result = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(requestId, receiptId, lineId, 10, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);
        return result.Outcome;
    }

    private static async Task<(World World, Guid TaskId)> ReceiveAndGetTaskAsync(int quantity = 10)
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var result = await CreateReceiptAsync(bundle, world, expected: quantity);
        var lineId = (await bundle.GetReceipt.Handle(result.ReceiptId, CancellationToken.None))!.Lines.Single().Id;
        var receive = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), result.ReceiptId, lineId, quantity, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);
        return (world, receive.PutawayTaskId);
    }

    private sealed class FkRow
    {
        public string TableName { get; set; } = string.Empty;

        public string FkSchema { get; set; } = string.Empty;

        public string FkTable { get; set; } = string.Empty;
    }

    private sealed record World(
        Guid Sku,
        string Barcode,
        Guid Warehouse,
        Guid Receiving,
        string ReceivingCode,
        Guid Storage,
        string StorageCode);

    private sealed class Bundle : IAsyncDisposable
    {
        private readonly InboundDbContext _inboundDb;
        private readonly InventoryDbContext _inventoryDb;
        private readonly FacilityDbContext _facilityDb;
        private readonly MasterDataDbContext _masterDb;

        public Bundle(
            InventoryStore inventoryStore,
            CreateReceipt createReceipt,
            ReceiveItems receiveItems,
            StartPutaway startPutaway,
            CompletePutaway completePutaway,
            CancelReceipt cancelReceipt,
            GetReceipt getReceipt,
            ListReceipts listReceipts,
            GetPutawayTask getPutawayTask,
            ListPutawayTasks listPutawayTasks,
            IInventoryContract inventoryContract,
            InboundDbContext inboundDb,
            InventoryDbContext inventoryDb,
            FacilityDbContext facilityDb,
            MasterDataDbContext masterDb)
        {
            InventoryStore = inventoryStore;
            CreateReceipt = createReceipt;
            ReceiveItems = receiveItems;
            StartPutaway = startPutaway;
            CompletePutaway = completePutaway;
            CancelReceipt = cancelReceipt;
            GetReceipt = getReceipt;
            ListReceipts = listReceipts;
            GetPutawayTask = getPutawayTask;
            ListPutawayTasks = listPutawayTasks;
            InventoryContract = inventoryContract;
            Reserve = new Reserve(inventoryStore, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
            _inboundDb = inboundDb;
            _inventoryDb = inventoryDb;
            _facilityDb = facilityDb;
            _masterDb = masterDb;
        }

        public InventoryStore InventoryStore { get; }

        public CreateReceipt CreateReceipt { get; }

        public ReceiveItems ReceiveItems { get; }

        public StartPutaway StartPutaway { get; }

        public CompletePutaway CompletePutaway { get; }

        public CancelReceipt CancelReceipt { get; }

        public GetReceipt GetReceipt { get; }

        public ListReceipts ListReceipts { get; }

        public GetPutawayTask GetPutawayTask { get; }

        public ListPutawayTasks ListPutawayTasks { get; }

        public IInventoryContract InventoryContract { get; }

        public Reserve Reserve { get; }

        public async ValueTask DisposeAsync()
        {
            await _inboundDb.DisposeAsync();
            await _inventoryDb.DisposeAsync();
            await _facilityDb.DisposeAsync();
            await _masterDb.DisposeAsync();
        }
    }
}
