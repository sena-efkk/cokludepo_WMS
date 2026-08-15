using Microsoft.EntityFrameworkCore;
using Wms.Modules.Facility.Domain;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Fulfillment.Application;
using Wms.Modules.Inbound.Application;
using Wms.Modules.Inbound.Contracts;
using Wms.Modules.Inbound.Infrastructure;
using Wms.Modules.Inbound.Infrastructure.Persistence;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Application.Accuracy;
using Wms.Modules.Inventory.Application.Accuracy.Scanning;
using Wms.Modules.Inventory.Contracts;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Infrastructure;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.MasterData.Infrastructure.Persistence;
using Wms.Modules.Outbound.Application;
using Wms.Modules.Outbound.Contracts;
using Wms.Modules.Outbound.Infrastructure;
using Wms.Modules.Outbound.Infrastructure.Persistence;
using Wms.Modules.Transfers.Application;
using Wms.Modules.Transfers.Contracts;
using Wms.Modules.Transfers.Domain;
using Wms.Modules.Transfers.Infrastructure;
using Wms.Modules.Transfers.Infrastructure.Persistence;
using Xunit;

namespace Wms.TransfersTests.Persistence;

public sealed class TransferIntegrationTests
{
    private static async Task<World> CreateWorldAsync(int sourceStock = 30)
    {
        var (sku, barcode) = await Db.CreateSkuWithBarcodeAsync();
        var sourceWarehouse = await Db.CreateWarehouseAsync();
        var destinationWarehouse = await Db.CreateWarehouseAsync();
        var (sourceLocation, sourceCode) = await Db.CreateStorageLocationAsync(sourceWarehouse);
        var (receivingLocation, receivingCode) = await Db.CreateStorageLocationAsync(destinationWarehouse, LocationType.Receiving);
        var (destStorage, destCode) = await Db.CreateStorageLocationAsync(destinationWarehouse);

        await OpenStockAsync(sourceWarehouse, sku, sourceLocation, sourceStock);

        return new World(
            sku,
            barcode,
            sourceWarehouse,
            destinationWarehouse,
            sourceLocation,
            sourceCode,
            receivingLocation,
            receivingCode,
            destStorage,
            destCode);
    }

    private static async Task OpenStockAsync(Guid warehouseId, Guid skuId, Guid locationId, int quantity)
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
            new RecordOpeningBalanceCommand(Guid.NewGuid(), skuId, warehouseId, locationId, InventoryStatus.Available, quantity),
            CancellationToken.None);
    }

    private static async Task<Bundle> CreateBundleAsync(World world)
    {
        var transfersDb = Db.CreateTransfersContext();
        var outboundDb = Db.CreateOutboundContext();
        var inboundDb = Db.CreateInboundContext();
        var inventoryDb = Db.CreateInventoryContext();
        var facilityDb = Db.CreateFacilityContext();
        var masterDb = Db.CreateMasterDataContext();

        var transferStore = new TransferStore(transfersDb);
        var outboundStore = new OutboundStore(outboundDb);
        var inboundStore = new InboundStore(inboundDb);
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

        var inboundContract = new InboundContractAdapter(
            new CreateReceipt(inboundStore, masterContract, facilityContract),
            new GetReceipt(inboundStore),
            new ReceiveItems(inboundStore, masterContract, facilityContract, inventoryContract, Microsoft.Extensions.Options.Options.Create(new InboundOptions())));

        var outboundContract = new OutboundContractAdapter(
            new CreateFulfillmentOrder(outboundStore, masterContract, facilityContract),
            new AllocateOrder(outboundStore, inventoryContract),
            new ShipOrder(outboundStore, inventoryContract),
            new GetOrder(outboundStore),
            new CancelOrder(outboundStore, inventoryContract));

        var transferContract = new TransferContractAdapter(transferStore);

        return new Bundle(
            transferStore,
            outboundStore,
            inboundStore,
            inventoryStore,
            transferContract,
            inventoryContract,
            inboundContract,
            outboundContract,
            new CreateTransfer(transferStore, masterContract, facilityContract),
            new AllocateTransfer(transferStore, outboundContract),
            new ShipTransfer(transferStore, outboundContract, inboundContract),
            new ReceiveTransfer(transferStore, inboundContract),
            new ConfirmTransferVariance(transferStore),
            new CancelTransfer(transferStore, outboundContract),
            new GetTransfer(transferStore),
            new ListTransfers(transferStore),
            new ConfirmPick(outboundStore, masterContract, facilityContract),
            new PackOrder(outboundStore),
            new GetOrder(outboundStore),
            new NetworkInventoryView(masterContract, inventoryContract, facilityContract, transferContract),
            transfersDb,
            outboundDb,
            inboundDb,
            inventoryDb,
            facilityDb,
            masterDb);
    }

    private static async Task<Guid> CreateTransferAsync(Bundle bundle, World world, int quantity = 10)
    {
        var result = await bundle.CreateTransfer.Handle(
            new CreateTransferCommand(
                Guid.NewGuid(),
                null,
                world.SourceWarehouse,
                world.DestinationWarehouse,
                "EXT-TRF",
                [new CreateTransferLineInput(world.Sku, quantity)]),
            CancellationToken.None);
        return result.TransferId;
    }

    private static async Task PickAndPackSourceOrderAsync(Bundle bundle, World world, Guid outboundOrderId)
    {
        var order = await bundle.GetOrder.Handle(outboundOrderId, CancellationToken.None);
        foreach (var task in order!.PickTasks)
        {
            await bundle.ConfirmPick.Handle(
                new ConfirmPickCommand(task.Id, world.SourceLocationCode, world.Barcode, task.RequiredQuantity),
                CancellationToken.None);
        }

        await bundle.PackOrder.Handle(new PackOrderCommand(outboundOrderId, Guid.NewGuid()), CancellationToken.None);
    }

    private static async Task<ShipTransferResult> AllocatePickPackAndShipAsync(Bundle bundle, World world, Guid transferId)
    {
        var allocate = await bundle.AllocateTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(AllocateTransferOutcome.Allocated, allocate.Outcome);
        await PickAndPackSourceOrderAsync(bundle, world, allocate.OutboundOrderId!.Value);
        return await bundle.ShipTransfer.Handle(new ShipTransferCommand(transferId, "TRK-1", "UPS"), CancellationToken.None);
    }

    // 1 — Source ve destination aynı warehouse olamaz.
    [Fact]
    public async Task Source_and_destination_cannot_be_same_warehouse()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);

        await Assert.ThrowsAsync<InvalidTransferStateException>(() => bundle.CreateTransfer.Handle(
            new CreateTransferCommand(Guid.NewGuid(), null, world.SourceWarehouse, world.SourceWarehouse, null,
                [new CreateTransferLineInput(world.Sku, 5)]),
            CancellationToken.None));
    }

    // 2 — Invalid warehouse reddedilir.
    [Fact]
    public async Task Invalid_warehouse_is_rejected()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);

        await Assert.ThrowsAsync<InvalidTransferStateException>(() => bundle.CreateTransfer.Handle(
            new CreateTransferCommand(Guid.NewGuid(), null, Guid.NewGuid(), world.DestinationWarehouse, null,
                [new CreateTransferLineInput(world.Sku, 5)]),
            CancellationToken.None));

        await Assert.ThrowsAsync<InvalidTransferStateException>(() => bundle.CreateTransfer.Handle(
            new CreateTransferCommand(Guid.NewGuid(), null, world.SourceWarehouse, Guid.NewGuid(), null,
                [new CreateTransferLineInput(world.Sku, 5)]),
            CancellationToken.None));
    }

    // 3 — Invalid SKU reddedilir.
    [Fact]
    public async Task Invalid_sku_is_rejected()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);

        await Assert.ThrowsAsync<InvalidTransferStateException>(() => bundle.CreateTransfer.Handle(
            new CreateTransferCommand(Guid.NewGuid(), null, world.SourceWarehouse, world.DestinationWarehouse, null,
                [new CreateTransferLineInput(Guid.NewGuid(), 5)]),
            CancellationToken.None));
    }

    // 4 — Transfer allocation source Inventory kullanır.
    [Fact]
    public async Task Allocation_uses_inventory_reservations()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);

        var result = await bundle.AllocateTransfer.Handle(transferId, CancellationToken.None);

        Assert.Equal(AllocateTransferOutcome.Allocated, result.Outcome);
        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(TransferStatus.Allocated, transfer!.Status);
        Assert.NotNull(transfer.OutboundOrderId);

        var outboundOrder = await bundle.GetOrder.Handle(transfer.OutboundOrderId!.Value, CancellationToken.None);
        var reservationId = Assert.Single(outboundOrder!.Lines).ReservationId;
        Assert.NotNull(reservationId);

        var detail = await bundle.InventoryContract.GetReservationAsync(reservationId!.Value, CancellationToken.None);
        Assert.Equal("ALLOCATED", detail!.Status);
        Assert.Equal(10, detail.Quantity);

        var sourceBalance = await bundle.InventoryStore.GetBalanceAsync(world.SourceWarehouse, world.Sku, world.SourceLocation, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(10, sourceBalance!.Allocated);
    }

    // 5 + 6 — Shipment source physical stock'u azaltır; InTransit doğru.
    [Fact]
    public async Task Shipment_decreases_source_stock_and_sets_in_transit()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);

        var ship = await AllocatePickPackAndShipAsync(bundle, world, transferId);

        Assert.Equal(ShipTransferOutcome.Shipped, ship.Outcome);
        var sourceBalance = await bundle.InventoryStore.GetBalanceAsync(world.SourceWarehouse, world.Sku, world.SourceLocation, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(20, sourceBalance!.Quantity);

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(TransferStatus.InTransit, transfer!.Status);
        Assert.Equal(10, transfer.InTransitQuantity);
        Assert.Equal(10, transfer.Lines.Single().ShippedQuantity);
        Assert.NotNull(transfer.InboundReceiptId);
        Assert.NotNull(transfer.ShippedAt);
    }

    // 7 — Network physical shipment sırasında sabit.
    [Fact]
    public async Task Network_physical_stays_constant_on_shipment()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);

        var before = await bundle.NetworkView.GetSkuAsync(world.Sku, CancellationToken.None);
        Assert.Equal(30, before!.NetworkPhysicalStock);

        await AllocatePickPackAndShipAsync(bundle, world, transferId);

        var after = await bundle.NetworkView.GetSkuAsync(world.Sku, CancellationToken.None);
        Assert.Equal(30, after!.NetworkPhysicalStock);
        Assert.Equal(20, after.NetworkAtp);
    }

    // 8 — InTransit ATP'ye dahil değil.
    [Fact]
    public async Task In_transit_is_excluded_from_atp()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);

        await AllocatePickPackAndShipAsync(bundle, world, transferId);

        var view = await bundle.NetworkView.GetSkuAsync(world.Sku, CancellationToken.None);
        Assert.Equal(20, view!.NetworkAtp);
        Assert.Equal(30, view.NetworkPhysicalStock);
        Assert.Equal(10, await bundle.TransferContract.GetOpenInTransitBySkuAsync(world.Sku, CancellationToken.None));
    }

    // 9 + 10 + 11 — Partial receive; destination inbound path; InTransit azalır.
    [Fact]
    public async Task Partial_receive_uses_inbound_path_and_reduces_in_transit()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);
        await AllocatePickPackAndShipAsync(bundle, world, transferId);

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        var lineId = transfer!.Lines.Single().Id;

        var receive = await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transferId, Guid.NewGuid(), lineId, 6, world.ReceivingLocation, "AVAILABLE"),
            CancellationToken.None);

        Assert.Equal(ReceiveTransferOutcome.Received, receive.Outcome);
        Assert.Equal(6, receive.LineReceivedQuantity);
        Assert.Equal(4, receive.LineInTransitQuantity);

        var after = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(TransferStatus.Receiving, after!.Status);
        Assert.Equal(4, after.InTransitQuantity);

        var destBalance = await bundle.InventoryStore.GetBalanceAsync(world.DestinationWarehouse, world.Sku, world.ReceivingLocation, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(6, destBalance!.Quantity);

        var ledger = await bundle.InventoryStore.ListLedgerAsync(world.DestinationWarehouse, world.Sku, null, 20, CancellationToken.None);
        Assert.Single(ledger, e => e.EntryType == LedgerEntryType.Received);

        var receipt = await bundle.InboundContract.GetReceiptAsync(after.InboundReceiptId!.Value, CancellationToken.None);
        Assert.Equal("PARTIALLY_RECEIVED", receipt!.Status);
    }

    // 12 — Full receipt sonrası InTransit zero; transfer COMPLETED.
    [Fact]
    public async Task Full_receipt_completes_transfer_with_zero_in_transit()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);
        await AllocatePickPackAndShipAsync(bundle, world, transferId);

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        var lineId = transfer!.Lines.Single().Id;

        await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transferId, Guid.NewGuid(), lineId, 6, world.ReceivingLocation, "AVAILABLE"),
            CancellationToken.None);
        await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transferId, Guid.NewGuid(), lineId, 4, world.ReceivingLocation, "AVAILABLE"),
            CancellationToken.None);

        var final = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(TransferStatus.Completed, final!.Status);
        Assert.Equal(0, final.InTransitQuantity);
        Assert.Equal(10, final.Lines.Single().ReceivedQuantity);
        Assert.NotNull(final.CompletedAt);

        var destBalance = await bundle.InventoryStore.GetBalanceAsync(world.DestinationWarehouse, world.Sku, world.ReceivingLocation, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(10, destBalance!.Quantity);
    }

    // 13 — Duplicate ship duplicate stock consumption yapmaz.
    [Fact]
    public async Task Duplicate_ship_does_not_double_consume()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);
        await AllocatePickPackAndShipAsync(bundle, world, transferId);

        var second = await bundle.ShipTransfer.Handle(new ShipTransferCommand(transferId, "TRK-1", "UPS"), CancellationToken.None);

        Assert.Equal(ShipTransferOutcome.AlreadyShipped, second.Outcome);
        var sourceBalance = await bundle.InventoryStore.GetBalanceAsync(world.SourceWarehouse, world.Sku, world.SourceLocation, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(20, sourceBalance!.Quantity);

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(10, transfer!.Lines.Single().ShippedQuantity);
    }

    // 14 — Duplicate receive duplicate stock oluşturmaz.
    [Fact]
    public async Task Duplicate_receive_does_not_double_stock()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);
        await AllocatePickPackAndShipAsync(bundle, world, transferId);

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        var lineId = transfer!.Lines.Single().Id;
        var requestId = Guid.NewGuid();

        var first = await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transferId, requestId, lineId, 6, world.ReceivingLocation, "AVAILABLE"),
            CancellationToken.None);
        var second = await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transferId, requestId, lineId, 6, world.ReceivingLocation, "AVAILABLE"),
            CancellationToken.None);

        Assert.Equal(ReceiveTransferOutcome.Received, first.Outcome);
        Assert.Equal(ReceiveTransferOutcome.AlreadyRecorded, second.Outcome);
        Assert.Equal(6, second.LineReceivedQuantity);

        var destBalance = await bundle.InventoryStore.GetBalanceAsync(world.DestinationWarehouse, world.Sku, world.ReceivingLocation, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(6, destBalance!.Quantity);
    }

    // 15 — Ship success + crash/retry güvenli.
    [Fact]
    public async Task Crash_after_outbound_ship_recovers_without_double_consumption()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);
        var allocate = await bundle.AllocateTransfer.Handle(transferId, CancellationToken.None);
        await PickAndPackSourceOrderAsync(bundle, world, allocate.OutboundOrderId!.Value);

        // Crash simülasyonu: outbound ship olur ama transfer state güncellenmez.
        var shipRequestId = CreateTransfer.DeriveChildRequestId(transferId, "SHIP");
        await bundle.OutboundContract.ShipOrderAsync(shipRequestId, allocate.OutboundOrderId.Value, null, null, CancellationToken.None);

        var retry = await bundle.ShipTransfer.Handle(new ShipTransferCommand(transferId), CancellationToken.None);

        Assert.Equal(ShipTransferOutcome.AlreadyShipped, retry.Outcome);
        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(TransferStatus.InTransit, transfer!.Status);
        Assert.Equal(10, transfer.InTransitQuantity);

        var sourceBalance = await bundle.InventoryStore.GetBalanceAsync(world.SourceWarehouse, world.Sku, world.SourceLocation, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(20, sourceBalance!.Quantity);
    }

    // 16 — Receive success + crash/retry güvenli.
    [Fact]
    public async Task Crash_after_inbound_receive_recovers_without_double_stock()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);
        await AllocatePickPackAndShipAsync(bundle, world, transferId);

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        var lineId = transfer!.Lines.Single().Id;
        var receiptLineId = transfer.Lines.Single().InboundReceiptLineId!.Value;
        var requestId = Guid.NewGuid();

        // Crash simülasyonu: Inbound receive olur ama transfer line güncellenmez.
        await bundle.InboundContract.ReceiveAsync(
            requestId,
            transfer.InboundReceiptId!.Value,
            receiptLineId,
            5,
            world.ReceivingLocation,
            "AVAILABLE",
            CancellationToken.None);

        var retry = await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transferId, requestId, lineId, 5, world.ReceivingLocation, "AVAILABLE"),
            CancellationToken.None);

        Assert.Equal(ReceiveTransferOutcome.Received, retry.Outcome);
        var after = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(5, after!.Lines.Single().ReceivedQuantity);
        Assert.Equal(5, after.InTransitQuantity);

        var destBalance = await bundle.InventoryStore.GetBalanceAsync(world.DestinationWarehouse, world.Sku, world.ReceivingLocation, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(5, destBalance!.Quantity);
    }

    // 17 — Short discrepancy korunur.
    [Fact]
    public async Task Short_discrepancy_is_preserved_and_closes_transfer()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);
        await AllocatePickPackAndShipAsync(bundle, world, transferId);

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        var lineId = transfer!.Lines.Single().Id;

        await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transferId, Guid.NewGuid(), lineId, 8, world.ReceivingLocation, "AVAILABLE"),
            CancellationToken.None);

        var variance = await bundle.ConfirmVariance.Handle(
            new ConfirmVarianceCommand(transferId, Guid.NewGuid(), lineId, 2, TransferDiscrepancyReason.Short, "short 2"),
            CancellationToken.None);

        Assert.Equal(ConfirmVarianceOutcome.Confirmed, variance.Outcome);
        Assert.True(variance.TransferCompleted);
        Assert.Equal(0, variance.LineInTransitQuantity);

        var final = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(TransferStatus.Completed, final!.Status);
        Assert.Equal(0, final.InTransitQuantity);
        var discrepancy = Assert.Single(final.Discrepancies);
        Assert.Equal(TransferDiscrepancyReason.Short, discrepancy.Reason);
        Assert.Equal(2, discrepancy.Quantity);
    }

    // 18 — Damaged/lost variance korunur.
    [Fact]
    public async Task Damaged_and_lost_variance_is_preserved()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);
        await AllocatePickPackAndShipAsync(bundle, world, transferId);

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        var lineId = transfer!.Lines.Single().Id;

        await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transferId, Guid.NewGuid(), lineId, 7, world.ReceivingLocation, "AVAILABLE"),
            CancellationToken.None);
        await bundle.ConfirmVariance.Handle(
            new ConfirmVarianceCommand(transferId, Guid.NewGuid(), lineId, 2, TransferDiscrepancyReason.DamagedInTransit),
            CancellationToken.None);
        await bundle.ConfirmVariance.Handle(
            new ConfirmVarianceCommand(transferId, Guid.NewGuid(), lineId, 1, TransferDiscrepancyReason.Lost),
            CancellationToken.None);

        var final = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(TransferStatus.Completed, final!.Status);
        Assert.Equal(0, final.InTransitQuantity);
        Assert.Equal(2, final.Discrepancies.Count);
        Assert.Contains(final.Discrepancies, d => d.Reason == TransferDiscrepancyReason.DamagedInTransit);
        Assert.Contains(final.Discrepancies, d => d.Reason == TransferDiscrepancyReason.Lost);
        Assert.Equal(3, final.Lines.Single().ConfirmedVarianceQuantity);
    }

    // 19 — Over receipt sessiz kabul edilmez.
    [Fact]
    public async Task Over_receipt_is_rejected()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);
        await AllocatePickPackAndShipAsync(bundle, world, transferId);

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        var lineId = transfer!.Lines.Single().Id;

        await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transferId, Guid.NewGuid(), lineId, 6, world.ReceivingLocation, "AVAILABLE"),
            CancellationToken.None);

        await Assert.ThrowsAsync<OverReceiptRejectedException>(() => bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transferId, Guid.NewGuid(), lineId, 5, world.ReceivingLocation, "AVAILABLE"),
            CancellationToken.None));

        var after = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(4, after!.InTransitQuantity);
        Assert.Equal(6, after.Lines.Single().ReceivedQuantity);
    }

    // 20 — Terminal transfer InTransit bırakmaz.
    [Fact]
    public async Task Terminal_transfer_has_no_dangling_in_transit()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);
        await AllocatePickPackAndShipAsync(bundle, world, transferId);

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        var lineId = transfer!.Lines.Single().Id;

        await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transferId, Guid.NewGuid(), lineId, 10, world.ReceivingLocation, "AVAILABLE"),
            CancellationToken.None);

        var final = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(TransferStatus.Completed, final!.Status);
        Assert.Equal(0, final.InTransitQuantity);
        Assert.True(final.Lines.All(l => l.IsClosed));
        Assert.Equal(0, await bundle.TransferContract.GetOpenInTransitBySkuAsync(world.Sku, CancellationToken.None));
    }

    // 21 — Shipment sonrası normal cancellation stok yok etmez.
    [Fact]
    public async Task Cancellation_after_shipment_is_rejected()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);
        await AllocatePickPackAndShipAsync(bundle, world, transferId);

        await Assert.ThrowsAsync<InvalidTransferStateException>(() => bundle.CancelTransfer.Handle(transferId, CancellationToken.None));

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(TransferStatus.InTransit, transfer!.Status);
        var sourceBalance = await bundle.InventoryStore.GetBalanceAsync(world.SourceWarehouse, world.Sku, world.SourceLocation, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(20, sourceBalance!.Quantity);
    }

    // 21b — Shipment öncesi cancel reservation release eder.
    [Fact]
    public async Task Cancellation_before_shipment_releases_reservations()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);
        var allocate = await bundle.AllocateTransfer.Handle(transferId, CancellationToken.None);

        await bundle.CancelTransfer.Handle(transferId, CancellationToken.None);

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(TransferStatus.Cancelled, transfer!.Status);

        var sourceBalance = await bundle.InventoryStore.GetBalanceAsync(world.SourceWarehouse, world.Sku, world.SourceLocation, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(0, sourceBalance!.Allocated);

        var outboundOrder = await bundle.GetOrder.Handle(allocate.OutboundOrderId!.Value, CancellationToken.None);
        Assert.Equal(Wms.Modules.Outbound.Domain.OrderStatus.Cancelled, outboundOrder!.Status);
    }

    // 22 — Network physical transfer boyunca korunur.
    [Fact]
    public async Task Network_physical_is_preserved_across_whole_transfer()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);

        var before = await bundle.NetworkView.GetSkuAsync(world.Sku, CancellationToken.None);
        var ship = await AllocatePickPackAndShipAsync(bundle, world, transferId);
        var afterShip = await bundle.NetworkView.GetSkuAsync(world.Sku, CancellationToken.None);

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        var lineId = transfer!.Lines.Single().Id;
        await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transferId, Guid.NewGuid(), lineId, 10, world.ReceivingLocation, "AVAILABLE"),
            CancellationToken.None);
        var afterReceive = await bundle.NetworkView.GetSkuAsync(world.Sku, CancellationToken.None);

        Assert.Equal(ShipTransferOutcome.Shipped, ship.Outcome);
        Assert.Equal(30, before!.NetworkPhysicalStock);
        Assert.Equal(30, afterShip!.NetworkPhysicalStock);
        Assert.Equal(30, afterReceive!.NetworkPhysicalStock);
    }

    // 23 — Network ATP in-transit'i hariç tutar.
    [Fact]
    public async Task Network_atp_excludes_in_transit()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);
        await AllocatePickPackAndShipAsync(bundle, world, transferId);

        var mid = await bundle.NetworkView.GetSkuAsync(world.Sku, CancellationToken.None);
        Assert.Equal(20, mid!.NetworkAtp);

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        var lineId = transfer!.Lines.Single().Id;
        await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transferId, Guid.NewGuid(), lineId, 10, world.ReceivingLocation, "AVAILABLE"),
            CancellationToken.None);

        var final = await bundle.NetworkView.GetSkuAsync(world.Sku, CancellationToken.None);
        Assert.Equal(30, final!.NetworkAtp);
    }

    // 24 — Cross-module DB FK yok.
    [Fact]
    public async Task Transfers_tables_have_no_cross_module_foreign_keys()
    {
        await using var db = Db.CreateTransfersContext();
        var rows = await db.Database.SqlQueryRaw<FkRow>(
                """
                SELECT tc.table_name AS "table_name", ccu.table_schema AS "fk_schema", ccu.table_name AS "fk_table"
                FROM information_schema.table_constraints tc
                JOIN information_schema.constraint_column_usage ccu ON tc.constraint_name = ccu.constraint_name
                WHERE tc.constraint_type = 'FOREIGN KEY'
                  AND tc.table_schema = 'transfers'
                """)
            .ToListAsync();

        Assert.All(rows, r => Assert.Equal("transfers", r.FkSchema));
    }

    // 25 — Gerçek PostgreSQL concurrency: parallel duplicate receive tek kayıt üretir.
    [Fact]
    public async Task Concurrent_duplicate_receives_produce_single_record()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync(world);
        var transferId = await CreateTransferAsync(bundle, world, 10);
        await AllocatePickPackAndShipAsync(bundle, world, transferId);

        var transfer = await bundle.GetTransfer.Handle(transferId, CancellationToken.None);
        var lineId = transfer!.Lines.Single().Id;
        var requestId = Guid.NewGuid();

        var outcomes = await Task.WhenAll(
            RunReceiveAsync(world, transferId, lineId, requestId),
            RunReceiveAsync(world, transferId, lineId, requestId));

        Assert.Equal(1, outcomes.Count(o => o == ReceiveTransferOutcome.Received));
        Assert.Equal(1, outcomes.Count(o => o == ReceiveTransferOutcome.AlreadyRecorded));

        await using var verifyBundle = await CreateBundleAsync(world);
        var final = await verifyBundle.GetTransfer.Handle(transferId, CancellationToken.None);
        Assert.Equal(5, final!.Lines.Single().ReceivedQuantity);

        await using var verifyInventoryDb = Db.CreateInventoryContext();
        var verifyStore = new InventoryStore(verifyInventoryDb);
        var destBalance = await verifyStore.GetBalanceAsync(world.DestinationWarehouse, world.Sku, world.ReceivingLocation, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(5, destBalance!.Quantity);

        await using var verifyDb = Db.CreateTransfersContext();
        var records = await verifyDb.TransferReceiveRecords.Where(r => r.RequestId == requestId).ToListAsync();
        Assert.Single(records);
    }

    private static async Task<ReceiveTransferOutcome> RunReceiveAsync(World world, Guid transferId, Guid lineId, Guid requestId)
    {
        await using var bundle = await CreateBundleAsync(world);
        var result = await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transferId, requestId, lineId, 5, world.ReceivingLocation, "AVAILABLE"),
            CancellationToken.None);
        return result.Outcome;
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
        Guid SourceWarehouse,
        Guid DestinationWarehouse,
        Guid SourceLocation,
        string SourceLocationCode,
        Guid ReceivingLocation,
        string ReceivingLocationCode,
        Guid DestStorage,
        string DestStorageCode);

    private sealed class Bundle : IAsyncDisposable
    {
        private readonly TransfersDbContext _transfersDb;
        private readonly OutboundDbContext _outboundDb;
        private readonly InboundDbContext _inboundDb;
        private readonly InventoryDbContext _inventoryDb;
        private readonly FacilityDbContext _facilityDb;
        private readonly MasterDataDbContext _masterDb;

        public Bundle(
            TransferStore transferStore,
            OutboundStore outboundStore,
            InboundStore inboundStore,
            InventoryStore inventoryStore,
            ITransferContract transferContract,
            IInventoryContract inventoryContract,
            IInboundContract inboundContract,
            IOutboundContract outboundContract,
            CreateTransfer createTransfer,
            AllocateTransfer allocateTransfer,
            ShipTransfer shipTransfer,
            ReceiveTransfer receiveTransfer,
            ConfirmTransferVariance confirmVariance,
            CancelTransfer cancelTransfer,
            GetTransfer getTransfer,
            ListTransfers listTransfers,
            ConfirmPick confirmPick,
            PackOrder packOrder,
            GetOrder getOrder,
            NetworkInventoryView networkView,
            TransfersDbContext transfersDb,
            OutboundDbContext outboundDb,
            InboundDbContext inboundDb,
            InventoryDbContext inventoryDb,
            FacilityDbContext facilityDb,
            MasterDataDbContext masterDb)
        {
            TransferStore = transferStore;
            OutboundStore = outboundStore;
            InboundStore = inboundStore;
            InventoryStore = inventoryStore;
            TransferContract = transferContract;
            InventoryContract = inventoryContract;
            InboundContract = inboundContract;
            OutboundContract = outboundContract;
            CreateTransfer = createTransfer;
            AllocateTransfer = allocateTransfer;
            ShipTransfer = shipTransfer;
            ReceiveTransfer = receiveTransfer;
            ConfirmVariance = confirmVariance;
            CancelTransfer = cancelTransfer;
            GetTransfer = getTransfer;
            ListTransfers = listTransfers;
            ConfirmPick = confirmPick;
            PackOrder = packOrder;
            GetOrder = getOrder;
            NetworkView = networkView;
            _transfersDb = transfersDb;
            _outboundDb = outboundDb;
            _inboundDb = inboundDb;
            _inventoryDb = inventoryDb;
            _facilityDb = facilityDb;
            _masterDb = masterDb;
        }

        public TransferStore TransferStore { get; }

        public OutboundStore OutboundStore { get; }

        public InboundStore InboundStore { get; }

        public InventoryStore InventoryStore { get; }

        public ITransferContract TransferContract { get; }

        public IInventoryContract InventoryContract { get; }

        public IInboundContract InboundContract { get; }

        public IOutboundContract OutboundContract { get; }

        public CreateTransfer CreateTransfer { get; }

        public AllocateTransfer AllocateTransfer { get; }

        public ShipTransfer ShipTransfer { get; }

        public ReceiveTransfer ReceiveTransfer { get; }

        public ConfirmTransferVariance ConfirmVariance { get; }

        public CancelTransfer CancelTransfer { get; }

        public GetTransfer GetTransfer { get; }

        public ListTransfers ListTransfers { get; }

        public ConfirmPick ConfirmPick { get; }

        public PackOrder PackOrder { get; }

        public GetOrder GetOrder { get; }

        public NetworkInventoryView NetworkView { get; }

        public async ValueTask DisposeAsync()
        {
            await _transfersDb.DisposeAsync();
            await _outboundDb.DisposeAsync();
            await _inboundDb.DisposeAsync();
            await _inventoryDb.DisposeAsync();
            await _facilityDb.DisposeAsync();
            await _masterDb.DisposeAsync();
        }
    }
}
