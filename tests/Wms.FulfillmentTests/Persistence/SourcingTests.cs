using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Fulfillment.Application;
using Wms.Modules.Fulfillment.Application.Optimization;
using Wms.Modules.Fulfillment.Domain;
using Wms.Modules.Fulfillment.Infrastructure.Persistence;
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
using Wms.Modules.Transfers.Infrastructure;
using Wms.Modules.Transfers.Infrastructure.Persistence;
using Xunit;

namespace Wms.FulfillmentTests.Persistence;

public sealed class SourcingTests
{
    private static async Task<Bundle> CreateBundleAsync()
    {
        var fulfillmentDb = Db.CreateFulfillmentContext();
        var outboundDb = Db.CreateOutboundContext();
        var inboundDb = Db.CreateInboundContext();
        var transfersDb = Db.CreateTransfersContext();
        var inventoryDb = Db.CreateInventoryContext();
        var facilityDb = Db.CreateFacilityContext();
        var masterDb = Db.CreateMasterDataContext();

        var fulfillmentStore = new FulfillmentStore(fulfillmentDb);
        var outboundStore = new OutboundStore(outboundDb);
        var inboundStore = new InboundStore(inboundDb);
        var transferStore = new TransferStore(transfersDb);
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
            new ReceiveItems(inboundStore, masterContract, facilityContract, inventoryContract, Options.Create(new InboundOptions())));

        var outboundContract = new OutboundContractAdapter(
            new CreateFulfillmentOrder(outboundStore, masterContract, facilityContract),
            new AllocateOrder(outboundStore, inventoryContract),
            new ShipOrder(outboundStore, inventoryContract),
            new GetOrder(outboundStore),
            new CancelOrder(outboundStore, inventoryContract));

        var transferContract = new TransferContractAdapter(transferStore);

        var optimizationOptions = new OptimizationOptions();
        var costModel = new FulfillmentCostModel(optimizationOptions);
        var optimizer = new SourcingOptimizer(
            optimizationOptions,
            new HaversineRouteProvider(),
            costModel);

        return new Bundle(
            fulfillmentStore,
            outboundStore,
            transferStore,
            inventoryStore,
            inventoryContract,
            outboundContract,
            inboundContract,
            transferContract,
            new EvaluateSourcing(
                fulfillmentStore,
                masterContract,
                facilityContract,
                inventoryContract,
                transferContract,
                Options.Create(new SourcingOptions()),
                optimizer),
            new CommitSourcingDecision(fulfillmentStore, outboundContract),
            new GetSourcing(fulfillmentStore),
            new CreateTransfer(transferStore, masterContract, facilityContract),
            new AllocateTransfer(transferStore, outboundContract),
            new ShipTransfer(transferStore, outboundContract, inboundContract),
            new ConfirmPick(outboundStore, masterContract, facilityContract),
            new PackOrder(outboundStore),
            new GetOrder(outboundStore),
            fulfillmentDb,
            outboundDb,
            inboundDb,
            transfersDb,
            inventoryDb,
            facilityDb,
            masterDb);
    }

    private static async Task<World> CreateWorldAsync()
    {
        var skuA = await Db.CreateSkuAsync();
        var skuB = await Db.CreateSkuAsync();
        var skuC = await Db.CreateSkuAsync();
        var warehouseA = await Db.CreateWarehouseAsync();
        var warehouseB = await Db.CreateWarehouseAsync();
        var warehouseC = await Db.CreateWarehouseAsync();
        var locA1 = await Db.CreateStorageLocationAsync(warehouseA);
        var locB1 = await Db.CreateStorageLocationAsync(warehouseB);
        var locC1 = await Db.CreateStorageLocationAsync(warehouseC);
        return new World(skuA, skuB, skuC, warehouseA, warehouseB, warehouseC, locA1.LocationId, locB1.LocationId, locC1.LocationId);
    }

    private static async Task OpenStockAsync(Guid warehouseId, Guid skuId, Guid locationId, InventoryStatus status, int quantity)
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

    private static EvaluateSourcingCommand Command(World world, (Guid Sku, int Qty)[] lines) =>
        new(Guid.NewGuid(), "BURSA-MERKEZ", lines.Select(l => new SourcingLineInput(l.Sku, l.Qty)).ToList());

    // 1 — Tek warehouse complete fulfillment.
    [Fact]
    public async Task Single_warehouse_complete_fulfillment_ranks_first()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, 5);
        await OpenStockAsync(world.WarehouseA, world.SkuB, world.LocA, InventoryStatus.Available, 4);
        await OpenStockAsync(world.WarehouseB, world.SkuA, world.LocB, InventoryStatus.Available, 10);
        await OpenStockAsync(world.WarehouseB, world.SkuB, world.LocB, InventoryStatus.Available, 10);
        await using var bundle = await CreateBundleAsync();

        var evaluation = await bundle.Evaluate.Handle(
            Command(world, [(world.SkuA, 2), (world.SkuB, 2)]),
            CancellationToken.None);

        Assert.True(evaluation.Fulfillable);
        var top = evaluation.Candidates[0];
        Assert.Equal(world.WarehouseB, top.WarehouseId);
        Assert.True(top.CanFulfillCompletely);
        Assert.Equal(2, top.FulfillableLineCount);
        Assert.Contains(top.Explanations, e => e.Contains("All 2 order lines available"));
        Assert.Contains(top.Explanations, e => e.Contains("Single warehouse"));
    }

    // 2 — İki warehouse complete ise deterministic ranking (daha yüksek ATP önce).
    [Fact]
    public async Task Deterministic_ranking_between_complete_warehouses()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, 5);
        await OpenStockAsync(world.WarehouseB, world.SkuA, world.LocB, InventoryStatus.Available, 10);
        await using var bundle = await CreateBundleAsync();

        var evaluation = await bundle.Evaluate.Handle(
            Command(world, [(world.SkuA, 3)]),
            CancellationToken.None);

        Assert.True(evaluation.Fulfillable);
        Assert.Equal(2, evaluation.Candidates.Count);
        Assert.Equal(world.WarehouseB, evaluation.Candidates[0].WarehouseId);
        Assert.Equal(world.WarehouseA, evaluation.Candidates[1].WarehouseId);
    }

    // 3 — Partial warehouse complete olarak işaretlenmez.
    [Fact]
    public async Task Partial_warehouse_is_not_complete()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, 3);
        await OpenStockAsync(world.WarehouseA, world.SkuB, world.LocA, InventoryStatus.Available, 10);
        await using var bundle = await CreateBundleAsync();

        var evaluation = await bundle.Evaluate.Handle(
            Command(world, [(world.SkuA, 5), (world.SkuB, 2)]),
            CancellationToken.None);

        var candidateA = evaluation.Candidates.Single(c => c.WarehouseId == world.WarehouseA);
        Assert.False(candidateA.CanFulfillCompletely);
        Assert.Equal(1, candidateA.FulfillableLineCount);
        Assert.Equal(2, candidateA.TotalLineCount);
    }

    // 4 — Network toplam ATP yetmiyorsa unfulfillable + shortage.
    [Fact]
    public async Task Network_shortage_is_explicit()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, 4);
        await OpenStockAsync(world.WarehouseB, world.SkuA, world.LocB, InventoryStatus.Available, 3);
        await using var bundle = await CreateBundleAsync();

        var evaluation = await bundle.Evaluate.Handle(
            Command(world, [(world.SkuA, 10)]),
            CancellationToken.None);

        Assert.False(evaluation.Fulfillable);
        var shortage = Assert.Single(evaluation.Shortages);
        Assert.Equal(world.SkuA, shortage.SkuId);
        Assert.Equal(10, shortage.RequestedQuantity);
        Assert.Equal(7, shortage.NetworkAtp);
        Assert.Equal(3, shortage.Shortage);
    }

    // 5 — Split plan oluşturulur.
    [Fact]
    public async Task Split_plan_is_generated_when_needed()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, 5);
        await OpenStockAsync(world.WarehouseB, world.SkuB, world.LocB, InventoryStatus.Available, 5);
        await using var bundle = await CreateBundleAsync();

        var evaluation = await bundle.Evaluate.Handle(
            Command(world, [(world.SkuA, 5), (world.SkuB, 5)]),
            CancellationToken.None);

        Assert.True(evaluation.Fulfillable);
        var split = evaluation.Candidates.First(c => c.Warehouses.Count == 2);
        Assert.True(split.CanFulfillCompletely);
        Assert.Contains(split.Explanations, e => e.Contains("split penalty"));
        Assert.Equal(2, split.Warehouses.Count);
    }

    // 6 — Max split limiti korunur.
    [Fact]
    public async Task Max_split_limit_is_enforced()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, 5);
        await OpenStockAsync(world.WarehouseB, world.SkuB, world.LocB, InventoryStatus.Available, 5);
        await OpenStockAsync(world.WarehouseC, world.SkuC, world.LocC, InventoryStatus.Available, 5);
        await using var bundle = await CreateBundleAsync();

        // MaxSplitWarehouses = 2; 3 line 3 ayrı warehouse gerektiriyor → 2'li hiçbir plan complete olamaz.
        var evaluation = await bundle.Evaluate.Handle(
            Command(world, [(world.SkuA, 5), (world.SkuB, 5), (world.SkuC, 5)]),
            CancellationToken.None);

        Assert.False(evaluation.Fulfillable);
        Assert.DoesNotContain(evaluation.Candidates, c => c.CanFulfillCompletely);
        Assert.DoesNotContain(evaluation.Candidates, c => c.Warehouses.Count > 2);
    }

    // 7 — Inactive warehouse candidate olmaz.
    [Fact]
    public async Task Inactive_warehouse_is_not_a_candidate()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, 10);
        await using (var facilityDb = Db.CreateFacilityContext())
        {
            var warehouse = await facilityDb.Warehouses.FirstAsync(w => w.Id == world.WarehouseA);
            warehouse.Deactivate();
            await facilityDb.SaveChangesAsync();
        }

        await using var bundle = await CreateBundleAsync();
        var evaluation = await bundle.Evaluate.Handle(
            Command(world, [(world.SkuA, 5)]),
            CancellationToken.None);

        Assert.DoesNotContain(evaluation.Candidates, c => c.WarehouseId == world.WarehouseA);
        Assert.False(evaluation.Fulfillable);
    }

    // 8 — HOLD/QUARANTINE/DAMAGED ATP'ye girmez.
    [Fact]
    public async Task Non_available_statuses_are_not_atp()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, 2);
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Hold, 10);
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Quarantine, 10);
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Damaged, 10);
        await using var bundle = await CreateBundleAsync();

        var evaluation = await bundle.Evaluate.Handle(
            Command(world, [(world.SkuA, 5)]),
            CancellationToken.None);

        var candidateA = evaluation.Candidates.Single(c => c.WarehouseId == world.WarehouseA);
        Assert.False(candidateA.CanFulfillCompletely);
        var line = candidateA.Warehouses.Single().Lines.Single();
        Assert.Equal(2, line.Atp);
        Assert.False(line.Fulfillable);
    }

    // 9 — InTransit ATP sayılmaz; IncomingStock context gösterilir.
    [Fact]
    public async Task In_transit_is_not_atp_but_shown_as_context()
    {
        var (skuA, barcodeA) = await Db.CreateSkuWithBarcodeAsync();
        var sourceWh = await Db.CreateWarehouseAsync();
        var destWh = await Db.CreateWarehouseAsync();
        var (sourceLoc, sourceCode) = await Db.CreateStorageLocationAsync(sourceWh);
        await Db.CreateLocationAsync(destWh, Wms.Modules.Facility.Domain.LocationType.Receiving);

        await OpenStockAsync(sourceWh, skuA, sourceLoc, InventoryStatus.Available, 20);

        await using var bundle = await CreateBundleAsync();

        var transfer = await bundle.CreateTransfer.Handle(
            new CreateTransferCommand(Guid.NewGuid(), null, sourceWh, destWh, null,
                [new CreateTransferLineInput(skuA, 10)]),
            CancellationToken.None);
        var allocate = await bundle.AllocateTransfer.Handle(transfer.TransferId, CancellationToken.None);

        var order = await bundle.GetOrder.Handle(allocate.OutboundOrderId!.Value, CancellationToken.None);
        foreach (var task in order!.PickTasks)
        {
            await bundle.ConfirmPick.Handle(new ConfirmPickCommand(task.Id, sourceCode, barcodeA, task.RequiredQuantity), CancellationToken.None);
        }

        await bundle.PackOrder.Handle(new PackOrderCommand(allocate.OutboundOrderId.Value, Guid.NewGuid()), CancellationToken.None);
        await bundle.ShipTransfer.Handle(new ShipTransferCommand(transfer.TransferId), CancellationToken.None);

        // DestWh'de ATP 0 ama InTransit 10.
        var evaluation = await bundle.Evaluate.Handle(
            new EvaluateSourcingCommand(Guid.NewGuid(), null, [new SourcingLineInput(skuA, 5)]),
            CancellationToken.None);

        Assert.DoesNotContain(evaluation.Candidates, c => c.WarehouseId == destWh && c.CanFulfillCompletely);
        var incoming = Assert.Single(evaluation.IncomingStock);
        Assert.Equal(skuA, incoming.SkuId);
        Assert.Equal(10, incoming.InTransitQuantity);
    }

    // 10 + 11 + 12 — RED risk cezalandırır ama ATP değişmez; explanation doğru.
    [Fact]
    public async Task Red_risk_penalizes_without_changing_atp()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, 10);
        await OpenStockAsync(world.WarehouseB, world.SkuA, world.LocB, InventoryStatus.Available, 10);

        // WarehouseA'da iki PickNotFound + eski stok → RED risk.
        await using (var inventoryDb = Db.CreateInventoryContext())
        {
            await inventoryDb.Database.ExecuteSqlRawAsync(
                "UPDATE inventory.inventory_ledger SET occurred_at = now() - interval '200 days' WHERE warehouse_id = {0} AND sku_id = {1}",
                world.WarehouseA,
                world.SkuA);
        }

        await using var signalBundle = await CreateBundleAsync();
        await signalBundle.InventoryContract.ReportPickNotFoundAsync(Guid.NewGuid(), world.SkuA, world.WarehouseA, world.LocA, null, CancellationToken.None);
        await signalBundle.InventoryContract.ReportPickNotFoundAsync(Guid.NewGuid(), world.SkuA, world.WarehouseA, world.LocA, null, CancellationToken.None);

        await using var bundle = await CreateBundleAsync();
        var evaluation = await bundle.Evaluate.Handle(
            Command(world, [(world.SkuA, 5)]),
            CancellationToken.None);

        var candidateA = evaluation.Candidates.Single(c => c.WarehouseId == world.WarehouseA);
        var candidateB = evaluation.Candidates.Single(c => c.WarehouseId == world.WarehouseB);

        Assert.Equal("RED", candidateA.WorstRiskLevel);
        Assert.Equal(10, candidateA.Warehouses.Single().Lines.Single().Atp);
        Assert.True(candidateA.Score < candidateB.Score);
        Assert.Equal(world.WarehouseB, evaluation.Candidates[0].WarehouseId);
        Assert.Contains(candidateA.Explanations, e => e.Contains("RED"));
        Assert.Contains(candidateA.Explanations, e => e.Contains("PickNotFound signals"));
    }

    // 13 — Evaluate stok mutate etmez.
    [Fact]
    public async Task Evaluate_does_not_mutate_stock()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, 10);
        await using var bundle = await CreateBundleAsync();

        await bundle.Evaluate.Handle(
            Command(world, [(world.SkuA, 5)]),
            CancellationToken.None);

        await using var verifyDb = Db.CreateInventoryContext();
        var verifyStore = new InventoryStore(verifyDb);
        var balance = await verifyStore.GetBalanceAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(10, balance!.Quantity);
        Assert.Equal(0, balance.Allocated);
    }

    // 14 + 15 — Commit reservation + Outbound order oluşturur.
    [Fact]
    public async Task Commit_creates_reservation_and_outbound_order()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, 10);
        await using var bundle = await CreateBundleAsync();

        var evaluation = await bundle.Evaluate.Handle(
            Command(world, [(world.SkuA, 5)]),
            CancellationToken.None);
        var selected = evaluation.Candidates.Single(c => c.CanFulfillCompletely);

        var commit = await bundle.Commit.Handle(
            new CommitSourcingCommand(
                Guid.NewGuid(),
                evaluation.SourcingRequestId,
                selected.Warehouses.Select(w => new CommitSourcingWarehouseInput(
                    w.WarehouseId,
                    w.Lines.Select(l => new CommitSourcingLineInput(l.SkuId, l.RequestedQuantity)).ToList())).ToList()),
            CancellationToken.None);

        Assert.Equal(SourcingCommitOutcome.Committed, commit.Outcome);
        var link = Assert.Single(commit.OrderLinks);
        Assert.Equal(world.WarehouseA, link.WarehouseId);

        var order = await bundle.GetOrder.Handle(link.OutboundOrderId, CancellationToken.None);
        Assert.Equal(world.SkuA, order!.Lines.Single().SkuId);
        Assert.NotNull(order.Lines.Single().ReservationId);

        var detail = await bundle.InventoryContract.GetReservationAsync(order.Lines.Single().ReservationId!.Value, CancellationToken.None);
        Assert.Equal("ALLOCATED", detail!.Status);
        Assert.Equal(5, detail.Quantity);

        var query = await bundle.GetSourcing.Handle(evaluation.SourcingRequestId, CancellationToken.None);
        Assert.Equal(SourcingStatus.Committed, query!.Status);
        Assert.NotNull(query.Decision);
    }

    // 16 — Split commit birden fazla warehouse order'ı oluşturur.
    [Fact]
    public async Task Split_commit_creates_multiple_orders()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, 5);
        await OpenStockAsync(world.WarehouseB, world.SkuB, world.LocB, InventoryStatus.Available, 5);
        await using var bundle = await CreateBundleAsync();

        var evaluation = await bundle.Evaluate.Handle(
            Command(world, [(world.SkuA, 5), (world.SkuB, 5)]),
            CancellationToken.None);
        var split = evaluation.Candidates.First(c => c.CanFulfillCompletely && c.Warehouses.Count == 2);

        var commit = await bundle.Commit.Handle(
            new CommitSourcingCommand(
                Guid.NewGuid(),
                evaluation.SourcingRequestId,
                split.Warehouses.Select(w => new CommitSourcingWarehouseInput(
                    w.WarehouseId,
                    w.Lines.Where(l => l.Fulfillable).Select(l => new CommitSourcingLineInput(l.SkuId, l.RequestedQuantity)).ToList())).ToList()),
            CancellationToken.None);

        Assert.Equal(SourcingCommitOutcome.Committed, commit.Outcome);
        Assert.Equal(2, commit.OrderLinks.Count);
        Assert.Contains(commit.OrderLinks, l => l.WarehouseId == world.WarehouseA);
        Assert.Contains(commit.OrderLinks, l => l.WarehouseId == world.WarehouseB);
    }

    // 17 — Evaluation sonrası stok değişirse commit SOURCING_STALE.
    [Fact]
    public async Task Stale_evaluation_fails_commit_safely()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, 10);
        await using var bundle = await CreateBundleAsync();

        var evaluation = await bundle.Evaluate.Handle(
            Command(world, [(world.SkuA, 5)]),
            CancellationToken.None);
        var selected = evaluation.Candidates.Single(c => c.CanFulfillCompletely);

        // Rakip sipariş stoğu alır.
        var rival = await bundle.InventoryContract.ReserveAsync(Guid.NewGuid(), world.SkuA, world.WarehouseA, 8, "rival", CancellationToken.None);
        Assert.True(rival.Quantity == 8);

        var commit = await bundle.Commit.Handle(
            new CommitSourcingCommand(
                Guid.NewGuid(),
                evaluation.SourcingRequestId,
                selected.Warehouses.Select(w => new CommitSourcingWarehouseInput(
                    w.WarehouseId,
                    w.Lines.Select(l => new CommitSourcingLineInput(l.SkuId, l.RequestedQuantity)).ToList())).ToList()),
            CancellationToken.None);

        Assert.Equal(SourcingCommitOutcome.Stale, commit.Outcome);
        Assert.NotNull(commit.StaleReason);

        var query = await bundle.GetSourcing.Handle(evaluation.SourcingRequestId, CancellationToken.None);
        Assert.Equal(SourcingStatus.Stale, query!.Status);
        Assert.Null(query.Decision);
    }

    // 18 — Duplicate commit duplicate reservation/order oluşturmaz.
    [Fact]
    public async Task Duplicate_commit_is_idempotent()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world.WarehouseA, world.SkuA, world.LocA, InventoryStatus.Available, 10);
        await using var bundle = await CreateBundleAsync();

        var evaluation = await bundle.Evaluate.Handle(
            Command(world, [(world.SkuA, 5)]),
            CancellationToken.None);
        var selected = evaluation.Candidates.Single(c => c.CanFulfillCompletely);

        var requestId = Guid.NewGuid();
        var plan = selected.Warehouses.Select(w => new CommitSourcingWarehouseInput(
            w.WarehouseId,
            w.Lines.Select(l => new CommitSourcingLineInput(l.SkuId, l.RequestedQuantity)).ToList())).ToList();

        var first = await bundle.Commit.Handle(new CommitSourcingCommand(requestId, evaluation.SourcingRequestId, plan), CancellationToken.None);
        var second = await bundle.Commit.Handle(new CommitSourcingCommand(requestId, evaluation.SourcingRequestId, plan), CancellationToken.None);

        Assert.Equal(SourcingCommitOutcome.Committed, first.Outcome);
        Assert.Equal(SourcingCommitOutcome.AlreadyCommitted, second.Outcome);
        Assert.Equal(first.DecisionId, second.DecisionId);
        Assert.Single(second.OrderLinks);

        await using var verifyDb = Db.CreateOutboundContext();
        var orderCount = await verifyDb.FulfillmentOrders.CountAsync(o => o.WarehouseId == world.WarehouseA);
        Assert.Equal(1, orderCount);

        await using var verifyInvDb = Db.CreateInventoryContext();
        var reservationCount = await verifyInvDb.InventoryReservations.CountAsync(r => r.WarehouseId == world.WarehouseA);
        Assert.Equal(1, reservationCount);
    }

    private sealed record World(
        Guid SkuA,
        Guid SkuB,
        Guid SkuC,
        Guid WarehouseA,
        Guid WarehouseB,
        Guid WarehouseC,
        Guid LocA,
        Guid LocB,
        Guid LocC);

    private sealed class Bundle : IAsyncDisposable
    {
        private readonly FulfillmentDbContext _fulfillmentDb;
        private readonly OutboundDbContext _outboundDb;
        private readonly InboundDbContext _inboundDb;
        private readonly TransfersDbContext _transfersDb;
        private readonly InventoryDbContext _inventoryDb;
        private readonly FacilityDbContext _facilityDb;
        private readonly MasterDataDbContext _masterDb;

        public Bundle(
            FulfillmentStore fulfillmentStore,
            OutboundStore outboundStore,
            TransferStore transferStore,
            InventoryStore inventoryStore,
            IInventoryContract inventoryContract,
            IOutboundContract outboundContract,
            IInboundContract inboundContract,
            ITransferContract transferContract,
            EvaluateSourcing evaluate,
            CommitSourcingDecision commit,
            GetSourcing getSourcing,
            CreateTransfer createTransfer,
            AllocateTransfer allocateTransfer,
            ShipTransfer shipTransfer,
            ConfirmPick confirmPick,
            PackOrder packOrder,
            GetOrder getOrder,
            FulfillmentDbContext fulfillmentDb,
            OutboundDbContext outboundDb,
            InboundDbContext inboundDb,
            TransfersDbContext transfersDb,
            InventoryDbContext inventoryDb,
            FacilityDbContext facilityDb,
            MasterDataDbContext masterDb)
        {
            FulfillmentStore = fulfillmentStore;
            OutboundStore = outboundStore;
            TransferStore = transferStore;
            InventoryStore = inventoryStore;
            InventoryContract = inventoryContract;
            OutboundContract = outboundContract;
            InboundContract = inboundContract;
            TransferContract = transferContract;
            Evaluate = evaluate;
            Commit = commit;
            GetSourcing = getSourcing;
            CreateTransfer = createTransfer;
            AllocateTransfer = allocateTransfer;
            ShipTransfer = shipTransfer;
            ConfirmPick = confirmPick;
            PackOrder = packOrder;
            GetOrder = getOrder;
            _fulfillmentDb = fulfillmentDb;
            _outboundDb = outboundDb;
            _inboundDb = inboundDb;
            _transfersDb = transfersDb;
            _inventoryDb = inventoryDb;
            _facilityDb = facilityDb;
            _masterDb = masterDb;
        }

        public FulfillmentStore FulfillmentStore { get; }

        public OutboundStore OutboundStore { get; }

        public TransferStore TransferStore { get; }

        public InventoryStore InventoryStore { get; }

        public IInventoryContract InventoryContract { get; }

        public IOutboundContract OutboundContract { get; }

        public IInboundContract InboundContract { get; }

        public ITransferContract TransferContract { get; }

        public EvaluateSourcing Evaluate { get; }

        public CommitSourcingDecision Commit { get; }

        public GetSourcing GetSourcing { get; }

        public CreateTransfer CreateTransfer { get; }

        public AllocateTransfer AllocateTransfer { get; }

        public ShipTransfer ShipTransfer { get; }

        public ConfirmPick ConfirmPick { get; }

        public PackOrder PackOrder { get; }

        public GetOrder GetOrder { get; }

        public async ValueTask DisposeAsync()
        {
            await _fulfillmentDb.DisposeAsync();
            await _outboundDb.DisposeAsync();
            await _inboundDb.DisposeAsync();
            await _transfersDb.DisposeAsync();
            await _inventoryDb.DisposeAsync();
            await _facilityDb.DisposeAsync();
            await _masterDb.DisposeAsync();
        }
    }
}
