using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Wms.Modules.Facility.Domain;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Fulfillment.Application;
using Wms.Modules.Fulfillment.Application.Optimization;
using Wms.Modules.Fulfillment.Infrastructure.Persistence;
using Wms.Modules.Inbound.Application;
using Wms.Modules.Inbound.Contracts;
using Wms.Modules.Inbound.Domain;
using Wms.Modules.Inbound.Infrastructure;
using Wms.Modules.Inbound.Infrastructure.Persistence;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Application.Accuracy;
using Wms.Modules.Inventory.Application.Accuracy.CycleCounting;
using Wms.Modules.Inventory.Application.Accuracy.Reconciliation;
using Wms.Modules.Inventory.Application.Accuracy.Scanning;
using Wms.Modules.Inventory.Contracts;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Domain.Accuracy.CycleCounting;
using Wms.Modules.Inventory.Domain.Accuracy.Reconciliation;
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

namespace Wms.IntegrationTests.Persistence;

public sealed class SystemIntegrityTests
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
            new ExecuteScannedRelocation(inventoryStore, masterContract, facilityContract, new RelocateStock(inventoryStore, masterContract, facilityContract)),
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
        var analyzer = new InventoryRiskAnalyzer(new RiskPolicyOptions());

        var optimizer = new SourcingOptimizer(
            new OptimizationOptions(),
            new HaversineRouteProvider(),
            new FulfillmentCostModel(new OptimizationOptions()));

        return new Bundle(
            fulfillmentStore,
            outboundStore,
            inboundStore,
            transferStore,
            inventoryStore,
            inventoryContract,
            inboundContract,
            outboundContract,
            transferContract,
            new CreateFulfillmentOrder(outboundStore, masterContract, facilityContract),
            new AllocateOrder(outboundStore, inventoryContract),
            new ConfirmPick(outboundStore, masterContract, facilityContract),
            new PackOrder(outboundStore),
            new GetOrder(outboundStore),
            new ShipOrder(outboundStore, inventoryContract),
            new CreateReceipt(inboundStore, masterContract, facilityContract),
            new ReceiveItems(inboundStore, masterContract, facilityContract, inventoryContract, Options.Create(new InboundOptions())),
            new CompletePutaway(inboundStore, masterContract, facilityContract, inventoryContract),
            new CreateTransfer(transferStore, masterContract, facilityContract),
            new AllocateTransfer(transferStore, outboundContract),
            new ShipTransfer(transferStore, outboundContract, inboundContract),
            new ReceiveTransfer(transferStore, inboundContract),
            new GetTransfer(transferStore),
            new EvaluateSourcing(fulfillmentStore, masterContract, facilityContract, inventoryContract, transferContract, Options.Create(new SourcingOptions()), optimizer),
            new CommitSourcingDecision(fulfillmentStore, outboundContract),
            new RecordOpeningBalance(inventoryStore, masterContract, facilityContract),
            new EvaluateCycleCountCandidates(inventoryStore, new ListRiskAssessments(inventoryStore, facilityContract, analyzer), analyzer),
            new StartCycleCount(inventoryStore),
            new CompleteCycleCount(inventoryStore, analyzer),
            new ApproveReconciliation(inventoryStore),
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
        var (sku, barcode) = await Db.CreateSkuWithBarcodeAsync();
        var warehouse = await Db.CreateWarehouseAsync();
        var (receiving, receivingCode) = await Db.CreateLocationAsync(warehouse, LocationType.Receiving);
        var (storage, storageCode) = await Db.CreateLocationAsync(warehouse, LocationType.Storage);
        return new World(sku, barcode, warehouse, receiving, receivingCode, storage, storageCode);
    }

    // 1 — NORMAL ORDER E2E: inbound → putaway → sourcing → reservation → pick → pack → ship → consumed.
    [Fact]
    public async Task Normal_order_end_to_end_flow_passes_all_invariants()
    {
        var world = await CreateWorldAsync();
        await using var bundle = await CreateBundleAsync();

        // Inbound receive.
        var receipt = await bundle.CreateReceipt.Handle(
            new CreateReceiptCommand(Guid.NewGuid(), null, world.Warehouse, null, "ASN", [new CreateReceiptLineInput(world.Sku, 10)]),
            CancellationToken.None);
        var receiptDetail = await bundle.InboundContract.GetReceiptAsync(receipt.ReceiptId, CancellationToken.None);
        var receive = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), receipt.ReceiptId, receiptDetail!.Lines.Single().Id, 10, world.Receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        // Putaway.
        await bundle.CompletePutaway.Handle(
            new CompletePutawayCommand(receive.PutawayTaskId, Guid.NewGuid(), world.ReceivingCode, world.Barcode, world.StorageCode, 10),
            CancellationToken.None);

        // Sourcing + commit.
        var evaluation = await bundle.EvaluateSourcing.Handle(
            new EvaluateSourcingCommand(Guid.NewGuid(), null, [new SourcingLineInput(world.Sku, 5)]),
            CancellationToken.None);
        Assert.True(evaluation.Fulfillable);
        var candidate = evaluation.Candidates.Single(c => c.CanFulfillCompletely);
        var commit = await bundle.CommitSourcing.Handle(
            new CommitSourcingCommand(
                Guid.NewGuid(),
                evaluation.SourcingRequestId,
                candidate.Warehouses.Select(w => new CommitSourcingWarehouseInput(w.WarehouseId, w.Lines.Where(l => l.Fulfillable).Select(l => new CommitSourcingLineInput(l.SkuId, l.RequestedQuantity)).ToList())).ToList()),
            CancellationToken.None);
        Assert.Equal(SourcingCommitOutcome.Committed, commit.Outcome);

        // Pick + pack + ship.
        var outboundOrderId = commit.OrderLinks.Single().OutboundOrderId;
        var order = await bundle.GetOrder.Handle(outboundOrderId, CancellationToken.None);
        foreach (var task in order!.PickTasks)
        {
            await bundle.ConfirmPick.Handle(new ConfirmPickCommand(task.Id, world.StorageCode, world.Barcode, task.RequiredQuantity), CancellationToken.None);
        }

        await bundle.PackOrder.Handle(new PackOrderCommand(outboundOrderId, Guid.NewGuid()), CancellationToken.None);
        await bundle.ShipOrder.Handle(new ShipOrderCommand(outboundOrderId, Guid.NewGuid(), "TRK", "UPS"), CancellationToken.None);

        // Final invariants.
        var finalOrder = await bundle.GetOrder.Handle(outboundOrderId, CancellationToken.None);
        Assert.Equal(Wms.Modules.Outbound.Domain.OrderStatus.Shipped, finalOrder!.Status);

        var reservationDetail = await bundle.InventoryContract.GetReservationAsync(
            finalOrder.Lines.Single().ReservationId!.Value, CancellationToken.None);
        Assert.Equal("CONSUMED", reservationDetail!.Status);

        var balance = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Storage, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(5, balance!.Quantity);
        Assert.Equal(0, balance.Allocated);

        var ledger = await bundle.InventoryStore.ListLedgerAsync(world.Warehouse, world.Sku, null, 50, CancellationToken.None);
        Assert.Contains(ledger, e => e.EntryType == LedgerEntryType.Received);
        Assert.Contains(ledger, e => e.EntryType == LedgerEntryType.Reserved);
        Assert.Contains(ledger, e => e.EntryType == LedgerEntryType.ReservationConsumed);
    }

    // 2 — PHANTOM INVENTORY: NotFound → risk → cycle count → variance → reconciliation → adjustment.
    [Fact]
    public async Task Phantom_inventory_scenario_corrects_only_via_reconciliation()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.Storage, InventoryStatus.Available, 10);
        await using var bundle = await CreateBundleAsync();

        // 2x PickNotFound — stok DEĞİŞMEZ.
        await bundle.InventoryContract.ReportPickNotFoundAsync(Guid.NewGuid(), world.Sku, world.Warehouse, world.Storage, null, CancellationToken.None);
        await bundle.InventoryContract.ReportPickNotFoundAsync(Guid.NewGuid(), world.Sku, world.Warehouse, world.Storage, null, CancellationToken.None);

        var beforeBalance = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Storage, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(10, beforeBalance!.Quantity);

        // Risk → cycle count (blind count) — stok DEĞİŞMEZ.
        var evaluate = await bundle.EvaluateCycleCounts.Handle(world.Warehouse, CancellationToken.None);
        Assert.True(evaluate.Created >= 1);

        var queue = await bundle.InventoryStore.GetCycleCountQueueAsync(world.Warehouse, 20, CancellationToken.None);
        var task = Assert.Single(queue, t => t.SkuId == world.Sku && t.LocationId == world.Storage);

        await bundle.StartCycleCount.Handle(task.Id, "op-1", CancellationToken.None);

        // Blind count: gerçekte 10 varken 7 sayılır → variance -3.
        var complete = await bundle.CompleteCycleCount.Handle(task.Id, 7, "op-1", CancellationToken.None);
        Assert.Equal(CountOutcome.VarianceDetected, complete.Outcome);

        var afterCountBalance = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Storage, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(10, afterCountBalance!.Quantity);

        // Reconciliation approve → controlled adjustment -3 → stok DÜZELİR (yalnız burası mutate eder).
        var reconciliation = await bundle.InventoryStore.GetReconciliationByResultIdAsync(complete.Id, CancellationToken.None);
        Assert.NotNull(reconciliation);

        var approval = await bundle.ApproveReconciliation.Handle(
            new ApproveReconciliationCommand(reconciliation!.Id, Guid.NewGuid(), AdjustmentReason.CycleCountVariance, "op-1", "phantom correction", Force: false),
            CancellationToken.None);

        Assert.Equal(ApprovalOutcome.Applied, approval.Outcome);

        var finalBalance = await bundle.InventoryStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Storage, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(7, finalBalance!.Quantity);

        var ledger = await bundle.InventoryStore.ListLedgerAsync(world.Warehouse, world.Sku, null, 50, CancellationToken.None);
        Assert.Contains(ledger, e => e.EntryType == LedgerEntryType.InventoryAdjustment);
    }

    // 3 — CONCURRENT LAST-STOCK: Available=1, 60 paralel reservation → TEK başarı.
    [Fact]
    public async Task Concurrent_last_stock_never_oversells()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.Storage, InventoryStatus.Available, 1);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 60)
                .Select(_ => RunReserveAsync(world, 1)));

        Assert.Equal(1, results.Count(r => r));

        await using var verifyDb = Db.CreateInventoryContext();
        var verifyStore = new InventoryStore(verifyDb);
        var balance = await verifyStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Storage, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(1, balance!.Quantity);
        Assert.Equal(1, balance.Allocated);
        Assert.Equal(0, balance.Available);
    }

    private static async Task<bool> RunReserveAsync(World world, int quantity)
    {
        await using var bundle = await CreateBundleAsync();
        try
        {
            await bundle.InventoryContract.ReserveAsync(Guid.NewGuid(), world.Sku, world.Warehouse, quantity, "hammer", CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // 4 — DUPLICATE REQUEST HAMMER: aynı RequestId yüksek concurrency → tek mutation.
    [Fact]
    public async Task Duplicate_request_hammer_produces_single_mutation()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.Storage, InventoryStatus.Available, 100);

        var openingRequestId = Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => RunOpeningAsync(world, openingRequestId, 10)));

        await using var verifyDb = Db.CreateInventoryContext();
        var verifyStore = new InventoryStore(verifyDb);
        var balance = await verifyStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Storage, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(110, balance!.Quantity);

        var reserveRequestId = Guid.NewGuid();
        var reserveOutcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => RunReserveWithRequestIdAsync(world, reserveRequestId, 5)));

        // Idempotent başarı: tüm retry'ler aynı sonucu görür ama TEK reservation oluşur.
        Assert.True(reserveOutcomes.Count(r => r) >= 1);

        await using var afterReserveDb = Db.CreateInventoryContext();
        var afterReserveStore = new InventoryStore(afterReserveDb);
        var afterReserve = await afterReserveStore.GetBalanceAsync(world.Warehouse, world.Sku, world.Storage, InventoryStatus.Available, CancellationToken.None);
        Assert.Equal(5, afterReserve!.Allocated);

        await using var verifyInvDb = Db.CreateInventoryContext();
        var reservationCount = await verifyInvDb.InventoryReservations.CountAsync(r => r.RequestId == reserveRequestId);
        Assert.Equal(1, reservationCount);
    }

    private static async Task RunOpeningAsync(World world, Guid requestId, int quantity)
    {
        await using var bundle = await CreateBundleAsync();
        await bundle.OpeningBalance.Handle(
            new RecordOpeningBalanceCommand(requestId, world.Sku, world.Warehouse, world.Storage, InventoryStatus.Available, quantity),
            CancellationToken.None);
    }

    private static async Task<bool> RunReserveWithRequestIdAsync(World world, Guid requestId, int quantity)
    {
        await using var bundle = await CreateBundleAsync();
        try
        {
            await bundle.InventoryContract.ReserveAsync(requestId, world.Sku, world.Warehouse, quantity, "dup", CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // 5 — TRANSFER INVARIANT: network physical her aşamada sabit.
    [Fact]
    public async Task Transfer_invariant_holds_at_every_stage()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.Storage, InventoryStatus.Available, 100);
        var destWarehouse = await Db.CreateWarehouseAsync();
        var (destReceiving, _) = await Db.CreateLocationAsync(destWarehouse, LocationType.Receiving);
        await using var bundle = await CreateBundleAsync();

        var before = await bundle.InventoryContract.ListSkuWarehouseAvailabilityAsync(world.Sku, CancellationToken.None);
        var physicalBefore = before.Sum(a => a.PhysicalStock);

        var transfer = await bundle.CreateTransfer.Handle(
            new CreateTransferCommand(Guid.NewGuid(), null, world.Warehouse, destWarehouse, null, [new CreateTransferLineInput(world.Sku, 20)]),
            CancellationToken.None);
        var allocate = await bundle.AllocateTransfer.Handle(transfer.TransferId, CancellationToken.None);

        var order = await bundle.GetOrder.Handle(allocate.OutboundOrderId!.Value, CancellationToken.None);
        foreach (var task in order!.PickTasks)
        {
            await bundle.ConfirmPick.Handle(new ConfirmPickCommand(task.Id, world.StorageCode, world.Barcode, task.RequiredQuantity), CancellationToken.None);
        }

        await bundle.PackOrder.Handle(new PackOrderCommand(allocate.OutboundOrderId.Value, Guid.NewGuid()), CancellationToken.None);
        await bundle.ShipTransfer.Handle(new ShipTransferCommand(transfer.TransferId), CancellationToken.None);

        var afterShip = await bundle.InventoryContract.ListSkuWarehouseAvailabilityAsync(world.Sku, CancellationToken.None);
        var physicalAfterShip = afterShip.Sum(a => a.PhysicalStock) + await bundle.TransferContract.GetOpenInTransitBySkuAsync(world.Sku, CancellationToken.None);
        Assert.Equal(physicalBefore, physicalAfterShip);

        var detail = await bundle.GetTransfer.Handle(transfer.TransferId, CancellationToken.None);
        var lineId = detail!.Lines.Single().Id;
        await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transfer.TransferId, Guid.NewGuid(), lineId, 12, destReceiving, "AVAILABLE"),
            CancellationToken.None);

        var afterPartial = await bundle.InventoryContract.ListSkuWarehouseAvailabilityAsync(world.Sku, CancellationToken.None);
        var physicalAfterPartial = afterPartial.Sum(a => a.PhysicalStock) + await bundle.TransferContract.GetOpenInTransitBySkuAsync(world.Sku, CancellationToken.None);
        Assert.Equal(physicalBefore, physicalAfterPartial);

        await bundle.ReceiveTransfer.Handle(
            new ReceiveTransferCommand(transfer.TransferId, Guid.NewGuid(), lineId, 8, destReceiving, "AVAILABLE"),
            CancellationToken.None);

        var final = await bundle.GetTransfer.Handle(transfer.TransferId, CancellationToken.None);
        Assert.Equal(TransferStatus.Completed, final!.Status);
        Assert.Equal(0, final.InTransitQuantity);

        var afterFinal = await bundle.InventoryContract.ListSkuWarehouseAvailabilityAsync(world.Sku, CancellationToken.None);
        Assert.Equal(physicalBefore, afterFinal.Sum(a => a.PhysicalStock));
    }

    // 6 — RABBITMQ DOWN → business OK + pending; UP → publish + consume + pending 0.
    [Fact]
    public async Task Broker_down_and_recovery_loses_no_event()
    {
        var world = await CreateWorldAsync();
        await OpenStockAsync(world, world.Storage, InventoryStatus.Available, 10);
        await using var bundle = await CreateBundleAsync();

        StopRabbitMq();

        Wms.Modules.Outbound.Application.ShipOrderResult ship;
        try
        {
            var created = await bundle.CreateOrder.Handle(
                new CreateFulfillmentOrderCommand(Guid.NewGuid(), null, world.Warehouse, null, [new CreateFulfillmentOrderLineInput(world.Sku, 3)]),
                CancellationToken.None);
            await bundle.AllocateOrder.Handle(created.OrderId, CancellationToken.None);
            var order = await bundle.GetOrder.Handle(created.OrderId, CancellationToken.None);
            foreach (var task in order!.PickTasks)
            {
                await bundle.ConfirmPick.Handle(new ConfirmPickCommand(task.Id, world.StorageCode, world.Barcode, task.RequiredQuantity), CancellationToken.None);
            }

            await bundle.PackOrder.Handle(new PackOrderCommand(created.OrderId, Guid.NewGuid()), CancellationToken.None);
            ship = await bundle.ShipOrder.Handle(new ShipOrderCommand(created.OrderId, Guid.NewGuid()), CancellationToken.None);

            // Broker down: business başarılı, outbox PENDING (bu shipment'ın eventi).
            await using (var verifyDb = Db.CreateOutboundContext())
            {
                var row = await verifyDb.OutboxMessages.SingleAsync(m => m.EventId == ship.ShipmentId);
                Assert.Null(row.PublishedAt);
            }
        }
        finally
        {
            StartRabbitMq();
        }

        // Broker recovery: önce container'ın HEALTHY olmasını bekle (backoff'a takılmamak için),
        // sonra dispatcher pending'i boşaltır.
        WaitForRabbitMqHealthy();

        var options = TestConfig.RabbitMqOptions();
        var publisher = new Wms.Integration.Messaging.RabbitMqPublisher(
            Options.Create(options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Wms.Integration.Messaging.RabbitMqPublisher>.Instance);
        var dispatcher = new Wms.Integration.Outbox.OutboxDispatcher(
            new FakeScopeFactory([bundle.OutboundStore, bundle.InboundStore]),
            publisher,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Wms.Integration.Outbox.OutboxDispatcher>.Instance);

        var deadline = DateTime.UtcNow.AddSeconds(90);
        var allPublished = false;
        while (DateTime.UtcNow < deadline)
        {
            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            await using var verifyDb = Db.CreateOutboundContext();
            var row = await verifyDb.OutboxMessages.SingleAsync(m => m.EventId == ship.ShipmentId);
            if (row.PublishedAt is not null)
            {
                allPublished = true;
                break;
            }

            await Task.Delay(3000);
        }

        Assert.True(allPublished, "Shipment event'i publish edilemedi — broker recovery başarısız.");
    }

    private sealed class FakeScopeFactory(IReadOnlyList<object> services) : IServiceScopeFactory
    {
        private sealed class FakeScope(IServiceProvider provider) : IServiceScope
        {
            public IServiceProvider ServiceProvider => provider;

            public void Dispose()
            {
            }
        }

        public IServiceScope CreateScope()
        {
            var collection = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            foreach (var service in services)
            {
                if (service is Wms.Integration.Outbox.IOutboxStore store)
                {
                    collection.AddSingleton(store);
                }
            }

            return new FakeScope(collection.BuildServiceProvider());
        }
    }

    // 7 — ARCH: duplicate writable stock modeli yalnız Inventory'de (Domain katmanı, mutable numeric state).
    [Fact]
    public void No_duplicate_writable_stock_models_outside_inventory()
    {
        var forbiddenModules = new[] { "Wms.Modules.Outbound", "Wms.Modules.Inbound", "Wms.Modules.Transfers", "Wms.Modules.Fulfillment" };
        var stockNames = new[] { "Quantity", "Stock", "Available", "Allocated" };
        var numericTypes = new HashSet<Type> { typeof(int), typeof(long), typeof(decimal), typeof(double) };

        var violations = new List<string>();

        foreach (var moduleName in forbiddenModules)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == moduleName);
            if (assembly is null)
            {
                continue;
            }

            foreach (var type in assembly.GetTypes())
            {
                if (type.Namespace is null || !type.Namespace.Contains(".Domain", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!type.IsClass || type.IsAbstract)
                {
                    continue;
                }

                foreach (var property in type.GetProperties())
                {
                    if (property.SetMethod is null || !property.SetMethod.IsPublic)
                    {
                        continue;
                    }

                    // init-only (record) setter'lar immutable'dır — read model değil, state değildir.
                    var isInitOnly = property.SetMethod.ReturnParameter
                        .GetRequiredCustomModifiers()
                        .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));
                    if (isInitOnly)
                    {
                        continue;
                    }

                    if (!numericTypes.Contains(property.PropertyType))
                    {
                        continue;
                    }

                    if (stockNames.Any(n => property.Name.Contains(n, StringComparison.Ordinal)))
                    {
                        violations.Add($"{moduleName} :: {type.FullName}.{property.Name}");
                    }
                }
            }
        }

        Assert.False(
            violations.Count > 0,
            "Inventory dışı modüllerde mutable numeric stock state bulundu (duplicate truth riski): " + string.Join("; ", violations));
    }

    // 8 — ARCH: cross-schema FK yok (tüm DB).
    [Fact]
    public async Task No_cross_schema_foreign_keys_exist()
    {
        await using var db = Db.CreateInventoryContext();
        var rows = await db.Database.SqlQueryRaw<CrossSchemaFk>(
                """
                SELECT tc.table_schema AS "fk_table_schema", tc.table_name AS "fk_table",
                       ccu.table_schema AS "referenced_schema", ccu.table_name AS "referenced_table"
                FROM information_schema.table_constraints tc
                JOIN information_schema.constraint_column_usage ccu ON tc.constraint_name = ccu.constraint_name
                WHERE tc.constraint_type = 'FOREIGN KEY'
                  AND tc.table_schema NOT IN ('pg_catalog', 'information_schema')
                  AND tc.table_schema <> ccu.table_schema
                """)
            .ToListAsync();

        Assert.Empty(rows);
    }

    private sealed class CrossSchemaFk
    {
        public string FkTableSchema { get; set; } = string.Empty;

        public string FkTable { get; set; } = string.Empty;

        public string ReferencedSchema { get; set; } = string.Empty;

        public string ReferencedTable { get; set; } = string.Empty;
    }

    private static void StopRabbitMq()
    {
        Run("docker", "stop wms-rabbitmq");
    }

    private static void StartRabbitMq()
    {
        Run("docker", "start wms-rabbitmq");
    }

    private static void WaitForRabbitMqHealthy()
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                Run("docker", "exec wms-rabbitmq rabbitmq-diagnostics -q ping");
                return;
            }
            catch
            {
                Task.Delay(3000).Wait();
            }
        }

        throw new InvalidOperationException("RabbitMQ 60 sn içinde healthy olmadı.");
    }

    private static void Run(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        process!.WaitForExit(60_000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{file} {arguments} başarısız (exit {process.ExitCode}): {process.StandardError.ReadToEnd()}");
        }
    }

    private static async Task OpenStockAsync(World world, Guid locationId, InventoryStatus status, int quantity)
    {
        await using var inventoryDb = Db.CreateInventoryContext();
        await using var facilityDb = Db.CreateFacilityContext();
        await using var masterDb = Db.CreateMasterDataContext();
        var store = new InventoryStore(inventoryDb);
        var opening = new RecordOpeningBalance(store, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
        await opening.Handle(
            new RecordOpeningBalanceCommand(Guid.NewGuid(), world.Sku, world.Warehouse, locationId, status, quantity),
            CancellationToken.None);
    }

    private sealed record World(Guid Sku, string Barcode, Guid Warehouse, Guid Receiving, string ReceivingCode, Guid Storage, string StorageCode);

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
            InboundStore inboundStore,
            TransferStore transferStore,
            InventoryStore inventoryStore,
            IInventoryContract inventoryContract,
            IInboundContract inboundContract,
            IOutboundContract outboundContract,
            ITransferContract transferContract,
            CreateFulfillmentOrder createOrder,
            AllocateOrder allocateOrder,
            ConfirmPick confirmPick,
            PackOrder packOrder,
            GetOrder getOrder,
            ShipOrder shipOrder,
            CreateReceipt createReceipt,
            ReceiveItems receiveItems,
            CompletePutaway completePutaway,
            CreateTransfer createTransfer,
            AllocateTransfer allocateTransfer,
            ShipTransfer shipTransfer,
            ReceiveTransfer receiveTransfer,
            GetTransfer getTransfer,
            EvaluateSourcing evaluateSourcing,
            CommitSourcingDecision commitSourcing,
            RecordOpeningBalance openingBalance,
            EvaluateCycleCountCandidates evaluateCycleCounts,
            StartCycleCount startCycleCount,
            CompleteCycleCount completeCycleCount,
            ApproveReconciliation approveReconciliation,
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
            InboundStore = inboundStore;
            TransferStore = transferStore;
            InventoryStore = inventoryStore;
            InventoryContract = inventoryContract;
            InboundContract = inboundContract;
            OutboundContract = outboundContract;
            TransferContract = transferContract;
            CreateOrder = createOrder;
            AllocateOrder = allocateOrder;
            ConfirmPick = confirmPick;
            PackOrder = packOrder;
            GetOrder = getOrder;
            ShipOrder = shipOrder;
            CreateReceipt = createReceipt;
            ReceiveItems = receiveItems;
            CompletePutaway = completePutaway;
            CreateTransfer = createTransfer;
            AllocateTransfer = allocateTransfer;
            ShipTransfer = shipTransfer;
            ReceiveTransfer = receiveTransfer;
            GetTransfer = getTransfer;
            EvaluateSourcing = evaluateSourcing;
            CommitSourcing = commitSourcing;
            OpeningBalance = openingBalance;
            EvaluateCycleCounts = evaluateCycleCounts;
            StartCycleCount = startCycleCount;
            CompleteCycleCount = completeCycleCount;
            ApproveReconciliation = approveReconciliation;
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

        public InboundStore InboundStore { get; }

        public TransferStore TransferStore { get; }

        public InventoryStore InventoryStore { get; }

        public IInventoryContract InventoryContract { get; }

        public IInboundContract InboundContract { get; }

        public IOutboundContract OutboundContract { get; }

        public ITransferContract TransferContract { get; }

        public CreateFulfillmentOrder CreateOrder { get; }

        public AllocateOrder AllocateOrder { get; }

        public ConfirmPick ConfirmPick { get; }

        public PackOrder PackOrder { get; }

        public GetOrder GetOrder { get; }

        public ShipOrder ShipOrder { get; }

        public CreateReceipt CreateReceipt { get; }

        public ReceiveItems ReceiveItems { get; }

        public CompletePutaway CompletePutaway { get; }

        public CreateTransfer CreateTransfer { get; }

        public AllocateTransfer AllocateTransfer { get; }

        public ShipTransfer ShipTransfer { get; }

        public ReceiveTransfer ReceiveTransfer { get; }

        public GetTransfer GetTransfer { get; }

        public EvaluateSourcing EvaluateSourcing { get; }

        public CommitSourcingDecision CommitSourcing { get; }

        public RecordOpeningBalance OpeningBalance { get; }

        public EvaluateCycleCountCandidates EvaluateCycleCounts { get; }

        public StartCycleCount StartCycleCount { get; }

        public CompleteCycleCount CompleteCycleCount { get; }

        public ApproveReconciliation ApproveReconciliation { get; }

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
