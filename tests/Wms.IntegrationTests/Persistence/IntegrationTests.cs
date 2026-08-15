using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Wms.Integration.Contracts;
using Wms.Integration.Messaging;
using Wms.Integration.Outbox;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Inbound.Application;
using Wms.Modules.Inbound.Contracts;
using Wms.Modules.Inbound.Domain;
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

namespace Wms.IntegrationTests.Persistence;

public sealed class IntegrationTests
{
    private static async Task<Bundle> CreateBundleAsync()
    {
        var outboundDb = Db.CreateOutboundContext();
        var inboundDb = Db.CreateInboundContext();
        var transfersDb = Db.CreateTransfersContext();
        var inventoryDb = Db.CreateInventoryContext();
        var facilityDb = Db.CreateFacilityContext();
        var masterDb = Db.CreateMasterDataContext();

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
            new ReceiveItems(inboundStore, masterContract, facilityContract, inventoryContract, Microsoft.Extensions.Options.Options.Create(new InboundOptions())));

        var outboundContract = new OutboundContractAdapter(
            new CreateFulfillmentOrder(outboundStore, masterContract, facilityContract),
            new AllocateOrder(outboundStore, inventoryContract),
            new ShipOrder(outboundStore, inventoryContract),
            new GetOrder(outboundStore),
            new CancelOrder(outboundStore, inventoryContract));

        return new Bundle(
            outboundDb,
            inboundDb,
            transfersDb,
            inventoryDb,
            facilityDb,
            masterDb,
            outboundStore,
            inboundStore,
            transferStore,
            inventoryStore,
            inventoryContract,
            inboundContract,
            outboundContract,
            new CreateFulfillmentOrder(outboundStore, masterContract, facilityContract),
            new AllocateOrder(outboundStore, inventoryContract),
            new ConfirmPick(outboundStore, masterContract, facilityContract),
            new PackOrder(outboundStore),
            new GetOrder(outboundStore),
            new ShipOrder(outboundStore, inventoryContract),
            new CreateReceipt(inboundStore, masterContract, facilityContract),
            new ReceiveItems(inboundStore, masterContract, facilityContract, inventoryContract, Microsoft.Extensions.Options.Options.Create(new InboundOptions())),
            new CompletePutaway(inboundStore, masterContract, facilityContract, inventoryContract),
            new CreateTransfer(transferStore, masterContract, facilityContract),
            new AllocateTransfer(transferStore, outboundContract),
            new ShipTransfer(transferStore, outboundContract, inboundContract),
            new ReceiveTransfer(transferStore, inboundContract),
            new GetTransfer(transferStore));
    }

    private static async Task<World> CreateShipWorldAsync(int stock = 20)
    {
        var (sku, barcode) = await Db.CreateSkuWithBarcodeAsync();
        var warehouse = await Db.CreateWarehouseAsync();
        var (location, locationCode) = await Db.CreateLocationAsync(warehouse, Wms.Modules.Facility.Domain.LocationType.Storage);

        await using var inventoryDb = Db.CreateInventoryContext();
        await using var facilityDb = Db.CreateFacilityContext();
        await using var masterDb = Db.CreateMasterDataContext();
        var store = new InventoryStore(inventoryDb);
        var opening = new RecordOpeningBalance(
            store,
            new MasterDataQueryContract(masterDb),
            new FacilityQueryContract(facilityDb));
        await opening.Handle(
            new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse, location, InventoryStatus.Available, stock),
            CancellationToken.None);

        return new World(sku, barcode, warehouse, location, locationCode);
    }

    private static async Task<(Guid OrderId, Guid ShipmentId)> CreateShippedOrderAsync(Bundle bundle, World world, int quantity)
    {
        var created = await bundle.CreateOrder.Handle(
            new CreateFulfillmentOrderCommand(Guid.NewGuid(), null, world.Warehouse, null,
                [new CreateFulfillmentOrderLineInput(world.Sku, quantity)]),
            CancellationToken.None);
        await bundle.AllocateOrder.Handle(created.OrderId, CancellationToken.None);

        var order = await bundle.GetOrder.Handle(created.OrderId, CancellationToken.None);
        foreach (var task in order!.PickTasks)
        {
            await bundle.ConfirmPick.Handle(
                new ConfirmPickCommand(task.Id, world.LocationCode, world.Barcode, task.RequiredQuantity),
                CancellationToken.None);
        }

        await bundle.PackOrder.Handle(new PackOrderCommand(created.OrderId, Guid.NewGuid()), CancellationToken.None);
        var ship = await bundle.ShipOrder.Handle(new ShipOrderCommand(created.OrderId, Guid.NewGuid(), "TRK-X", "UPS"), CancellationToken.None);
        return (created.OrderId, ship.ShipmentId);
    }

    // 1 — Business transaction + outbox aynı transaction'da.
    [Fact]
    public async Task Shipment_and_outbox_are_atomic()
    {
        var world = await CreateShipWorldAsync();
        await using var bundle = await CreateBundleAsync();

        var (_, shipmentId) = await CreateShippedOrderAsync(bundle, world, 5);

        await using var verifyDb = Db.CreateOutboundContext();
        var message = await verifyDb.OutboxMessages.SingleAsync(m => m.EventId == shipmentId);
        Assert.Equal(IntegrationEventTypes.ShipmentShipped, message.EventType);
        Assert.Null(message.PublishedAt);
        var payload = JsonSerializer.Deserialize<ShipmentShippedV1>(message.Payload);
        Assert.NotNull(payload);
        Assert.Equal(payload!.WarehouseId, world.Warehouse);
        Assert.Equal(IntegrationEventTypes.CurrentVersion, message.EventVersion);
    }

    // 2 — Business rollback → outbox oluşmaz.
    [Fact]
    public async Task Failed_shipment_produces_no_outbox()
    {
        var world = await CreateShipWorldAsync();
        await using var bundle = await CreateBundleAsync();

        var created = await bundle.CreateOrder.Handle(
            new CreateFulfillmentOrderCommand(Guid.NewGuid(), null, world.Warehouse, null,
                [new CreateFulfillmentOrderLineInput(world.Sku, 3)]),
            CancellationToken.None);

        await using (var beforeDb = Db.CreateOutboundContext())
        {
            var before = await beforeDb.OutboxMessages.CountAsync();
            await Assert.ThrowsAsync<InvalidOrderStateException>(() => bundle.ShipOrder.Handle(
                new ShipOrderCommand(created.OrderId, Guid.NewGuid()),
                CancellationToken.None));

            var after = await beforeDb.OutboxMessages.CountAsync();
            Assert.Equal(before, after);
        }
    }

    // 3 — Shipment success → ShipmentShipped outbox (payload korelasyonu).
    [Fact]
    public async Task Shipment_outbox_carries_order_correlation()
    {
        var world = await CreateShipWorldAsync();
        await using var bundle = await CreateBundleAsync();

        var (orderId, shipmentId) = await CreateShippedOrderAsync(bundle, world, 4);

        await using var verifyDb = Db.CreateOutboundContext();
        var message = await verifyDb.OutboxMessages.SingleAsync(m => m.EventId == shipmentId);
        var payload = JsonSerializer.Deserialize<ShipmentShippedV1>(message.Payload);
        Assert.Equal(orderId, payload!.OrderId);
        Assert.Equal(orderId, payload.CorrelationId);
        Assert.Equal(message.EventId, payload.EventId);
    }

    // 4 — Receipt completion → ReceiptCompleted outbox.
    [Fact]
    public async Task Receipt_completion_produces_receipt_completed_outbox()
    {
        var (sku, barcode) = await Db.CreateSkuWithBarcodeAsync();
        var warehouse = await Db.CreateWarehouseAsync();
        var (receiving, receivingCode) = await Db.CreateLocationAsync(warehouse, Wms.Modules.Facility.Domain.LocationType.Receiving);
        var (storage, storageCode) = await Db.CreateLocationAsync(warehouse, Wms.Modules.Facility.Domain.LocationType.Storage);
        await using var bundle = await CreateBundleAsync();

        var receipt = await bundle.CreateReceipt.Handle(
            new CreateReceiptCommand(Guid.NewGuid(), null, warehouse, null, "ASN",
                [new CreateReceiptLineInput(sku, 10)]),
            CancellationToken.None);
        var receiptDetail = await bundle.InboundContract.GetReceiptAsync(receipt.ReceiptId, CancellationToken.None);
        var lineId = receiptDetail!.Lines.Single().Id;

        var receive = await bundle.ReceiveItems.Handle(
            new ReceiveItemsCommand(Guid.NewGuid(), receipt.ReceiptId, lineId, 10, receiving, ReceivingStockStatus.Available),
            CancellationToken.None);

        await bundle.CompletePutaway.Handle(
            new CompletePutawayCommand(receive.PutawayTaskId, Guid.NewGuid(), receivingCode, barcode, storageCode, 10),
            CancellationToken.None);

        await using var verifyDb = Db.CreateInboundContext();
        var message = await verifyDb.OutboxMessages.SingleAsync(m => m.EventId == receipt.ReceiptId);
        Assert.Equal(IntegrationEventTypes.ReceiptCompleted, message.EventType);
        var payload = JsonSerializer.Deserialize<ReceiptCompletedV1>(message.Payload);
        Assert.Equal(receipt.ReceiptId, payload!.ReceiptId);
        Assert.Equal(warehouse, payload.WarehouseId);
    }

    // 5 + 6 — Dispatcher publish eder; published tekrar dispatch edilmez.
    [Fact]
    public async Task Dispatcher_publishes_pending_and_skips_published()
    {
        var world = await CreateShipWorldAsync();
        await using var bundle = await CreateBundleAsync();
        var (_, shipmentId) = await CreateShippedOrderAsync(bundle, world, 5);

        var fake = new FakePublisher();
        var dispatcher = new OutboxDispatcher(
            CreateScopeFactory(bundle.OutboundStore),
            fake,
            NullLogger<OutboxDispatcher>.Instance);

        var first = await dispatcher.DispatchOnceAsync(CancellationToken.None);
        Assert.True(first.Dispatched >= 1);
        Assert.Single(fake.Published, m => m.EventId == shipmentId);

        var second = await dispatcher.DispatchOnceAsync(CancellationToken.None);
        Assert.Single(fake.Published, m => m.EventId == shipmentId);

        await using var verifyDb = Db.CreateOutboundContext();
        var message = await verifyDb.OutboxMessages.SingleAsync(m => m.EventId == shipmentId);
        Assert.NotNull(message.PublishedAt);
        Assert.Equal(1, message.AttemptCount);
    }

    // 7 — Broker down → business transaction kaybolmaz; publish failure metadata yazılır.
    [Fact]
    public async Task Broker_down_keeps_business_and_marks_publish_failure()
    {
        var world = await CreateShipWorldAsync();
        await using var bundle = await CreateBundleAsync();
        var (_, shipmentId) = await CreateShippedOrderAsync(bundle, world, 5);

        var badOptions = TestConfig.RabbitMqOptions();
        badOptions.Port = 5999;
        badOptions.ConnectionTimeout = TimeSpan.FromSeconds(1);

        var badPublisher = new RabbitMqPublisher(
            Options.Create(badOptions),
            NullLogger<RabbitMqPublisher>.Instance);

        var dispatcher = new OutboxDispatcher(
            CreateScopeFactory(bundle.OutboundStore),
            badPublisher,
            NullLogger<OutboxDispatcher>.Instance);

        var result = await dispatcher.DispatchOnceAsync(CancellationToken.None);
        Assert.True(result.Failed >= 1);
        Assert.Equal(0, result.Dispatched);

        await using var verifyDb = Db.CreateOutboundContext();
        var message = await verifyDb.OutboxMessages.SingleAsync(m => m.EventId == shipmentId);
        Assert.Null(message.PublishedAt);
        Assert.Equal(1, message.AttemptCount);
        Assert.NotNull(message.LastError);
        Assert.NotNull(message.NextAttemptAt);
    }

    // 8 — Broker recovery → pending event publish edilir ve gerçek kuyruktan okunur.
    [Fact]
    public async Task Broker_recovery_publishes_pending_event_to_real_queue()
    {
        var world = await CreateShipWorldAsync();
        await using var bundle = await CreateBundleAsync();
        var (_, shipmentId) = await CreateShippedOrderAsync(bundle, world, 5);

        var options = TestConfig.RabbitMqOptions();
        var queueName = $"test-inbox-{Guid.NewGuid():N}";
        var routingKey = $"{IntegrationEventTypes.ShipmentShipped}.v{IntegrationEventTypes.CurrentVersion}";

        var publisher = new RabbitMqPublisher(Options.Create(options), NullLogger<RabbitMqPublisher>.Instance);
        await publisher.DeclareQueueAsync(queueName, routingKey, CancellationToken.None);

        var dispatcher = new OutboxDispatcher(
            CreateScopeFactory(bundle.OutboundStore),
            publisher,
            NullLogger<OutboxDispatcher>.Instance);
        var result = await dispatcher.DispatchOnceAsync(CancellationToken.None);

        Assert.True(result.Dispatched >= 1);

        var factory = new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            UserName = options.Username,
            Password = options.Password,
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        BasicGetResult? received = null;
        for (var i = 0; i < 10 && received is null; i++)
        {
            received = await channel.BasicGetAsync(queueName, autoAck: true, CancellationToken.None);
            if (received is null)
            {
                await Task.Delay(200);
            }
        }

        Assert.NotNull(received);
        var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope>(Encoding.UTF8.GetString(received.Body.ToArray()));
        Assert.Equal(IntegrationEventTypes.ShipmentShipped, envelope!.EventType);

        await using var verifyDb = Db.CreateOutboundContext();
        var message = await verifyDb.OutboxMessages.SingleAsync(m => m.EventId == shipmentId);
        Assert.NotNull(message.PublishedAt);
    }

    // 9-11 — Duplicate delivery: consumer Inbox EventId duplicate'i engeller (business effect 1 kez).
    [Fact]
    public async Task Transfer_consumer_handles_duplicate_shipment_event_once()
    {
        var (sku, barcode) = await Db.CreateSkuWithBarcodeAsync();
        var sourceWarehouse = await Db.CreateWarehouseAsync();
        var destWarehouse = await Db.CreateWarehouseAsync();
        var (sourceLoc, sourceCode) = await Db.CreateLocationAsync(sourceWarehouse, Wms.Modules.Facility.Domain.LocationType.Storage);
        var (destReceiving, _) = await Db.CreateLocationAsync(destWarehouse, Wms.Modules.Facility.Domain.LocationType.Receiving);

        await using (var inventoryDb = Db.CreateInventoryContext())
        await using (var facilityDb = Db.CreateFacilityContext())
        await using (var masterDb = Db.CreateMasterDataContext())
        {
            var store = new InventoryStore(inventoryDb);
            var opening = new RecordOpeningBalance(store, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
            await opening.Handle(new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, sourceWarehouse, sourceLoc, InventoryStatus.Available, 20), CancellationToken.None);
        }

        await using var bundle = await CreateBundleAsync();

        var transfer = await bundle.CreateTransfer.Handle(
            new CreateTransferCommand(Guid.NewGuid(), null, sourceWarehouse, destWarehouse, null,
                [new CreateTransferLineInput(sku, 5)]),
            CancellationToken.None);
        var allocate = await bundle.AllocateTransfer.Handle(transfer.TransferId, CancellationToken.None);
        var outboundOrderId = allocate.OutboundOrderId!.Value;

        // Event üretimi: source order ship edildi (biz doğrudan outbound üzerinden simüle ediyoruz).
        var order = await bundle.GetOrder.Handle(outboundOrderId, CancellationToken.None);
        foreach (var task in order!.PickTasks)
        {
            await bundle.ConfirmPick.Handle(new ConfirmPickCommand(task.Id, sourceCode, barcode, task.RequiredQuantity), CancellationToken.None);
        }

        await bundle.PackOrder.Handle(new PackOrderCommand(outboundOrderId, Guid.NewGuid()), CancellationToken.None);
        var ship = await bundle.ShipOrder.Handle(new ShipOrderCommand(outboundOrderId, Guid.NewGuid(), null, "UPS"), CancellationToken.None);

        var consumer = new TransferEventConsumer(
            bundle.TransferStore,
            bundle.ShipTransfer,
            NullLogger<TransferEventConsumer>.Instance);

        var envelope = new IntegrationEventEnvelope(
            ship.ShipmentId,
            IntegrationEventTypes.ShipmentShipped,
            1,
            DateTime.UtcNow,
            outboundOrderId,
            JsonSerializer.Serialize(new ShipmentShippedV1(ship.ShipmentId, DateTime.UtcNow, ship.ShipmentId, outboundOrderId, order.OrderNumber, sourceWarehouse, outboundOrderId)));

        var first = await consumer.HandleAsync(envelope, CancellationToken.None);
        var second = await consumer.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal(ConsumerProcessingResult.Ack, first);
        Assert.Equal(ConsumerProcessingResult.Ack, second);

        var final = await bundle.GetTransfer.Handle(transfer.TransferId, CancellationToken.None);
        Assert.Equal(Wms.Modules.Transfers.Domain.TransferStatus.InTransit, final!.Status);
        Assert.Equal(5, final.Lines.Single().ShippedQuantity);

        await using var verifyDb = Db.CreateTransfersContext();
        var inbox = await verifyDb.InboxMessages.Where(m => m.EventId == envelope.EventId).ToListAsync();
        Assert.Single(inbox);
    }

    // 12 — DLQ: poison message policy sonrası DLQ'ya düşer.
    [Fact]
    public async Task Dlq_catches_poison_messages()
    {
        var options = TestConfig.RabbitMqOptions();
        var queueName = $"poison-test-{Guid.NewGuid():N}";

        var factory = new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            UserName = options.Username,
            Password = options.Password,
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: false,
            exclusive: true,
            autoDelete: true,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = options.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = queueName,
            });

        await channel.ExchangeDeclareAsync(options.DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, arguments: null);
        await channel.QueueDeclareAsync($"{queueName}-dlq", durable: false, exclusive: true, autoDelete: true, arguments: null);
        await channel.QueueBindAsync($"{queueName}-dlq", options.DeadLetterExchange, queueName, arguments: null);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queueName,
            body: Encoding.UTF8.GetBytes("poison"),
            cancellationToken: CancellationToken.None);

        var received = await channel.BasicGetAsync(queueName, autoAck: false, CancellationToken.None);
        Assert.NotNull(received);
        await channel.BasicNackAsync(received.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);

        await Task.Delay(500);
        var dead = await channel.BasicGetAsync($"{queueName}-dlq", autoAck: true, CancellationToken.None);
        Assert.NotNull(dead);
    }

    // 13 — Unknown event tipi consumer'ı bozmaz.
    [Fact]
    public async Task Unknown_event_type_is_ignored_gracefully()
    {
        await using var bundle = await CreateBundleAsync();
        var consumer = new TransferEventConsumer(
            bundle.TransferStore,
            bundle.ShipTransfer,
            NullLogger<TransferEventConsumer>.Instance);

        var envelope = new IntegrationEventEnvelope(
            Guid.NewGuid(),
            "unknown.event-type",
            1,
            DateTime.UtcNow,
            null,
            "{}");

        var result = await consumer.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal(ConsumerProcessingResult.Ack, result);
    }

    // 14 — Event contract domain entity serialization değildir (yalnız primitive alanlar).
    [Fact]
    public void Event_contracts_contain_only_primitive_fields()
    {
        var allowed = new HashSet<Type> { typeof(Guid), typeof(string), typeof(int), typeof(DateTime), typeof(decimal), typeof(bool), typeof(long), typeof(byte[]) };
        allowed.UnionWith(new[] { typeof(Guid?), typeof(int?), typeof(decimal?), typeof(DateTime?) });

        foreach (var type in new[] { typeof(ShipmentShippedV1), typeof(ReceiptCompletedV1) })
        {
            foreach (var property in type.GetProperties())
            {
                Assert.Contains(property.PropertyType, allowed);
            }
        }
    }

    // 15 — Event direction: Outbound/Inbound üretir, Transfers tüketir (assembly referansları).
    [Fact]
    public void Event_direction_is_producer_to_consumer()
    {
        var outbound = typeof(OutboundContractAdapter).Assembly;
        var inbound = typeof(InboundContractAdapter).Assembly;
        var transfers = typeof(TransferContractAdapter).Assembly;
        var integration = typeof(IntegrationEventTypes).Assembly;

        Assert.DoesNotContain(outbound.GetReferencedAssemblies(), a => a.Name == "Wms.Modules.Transfers");
        Assert.DoesNotContain(inbound.GetReferencedAssemblies(), a => a.Name == "Wms.Modules.Transfers");
        Assert.Contains(outbound.GetReferencedAssemblies(), a => a.Name == "Wms.Integration");
        Assert.Contains(inbound.GetReferencedAssemblies(), a => a.Name == "Wms.Integration");
        Assert.Contains(transfers.GetReferencedAssemblies(), a => a.Name == "Wms.Integration");
        Assert.Equal("Wms.Integration", integration.GetName().Name);
    }

    // 16-17 — Transfers duplicate ReceiptCompleted işlemez (inbox + idempotent handler).
    [Fact]
    public async Task Transfer_consumer_handles_duplicate_receipt_event_once()
    {
        var (sku, barcode) = await Db.CreateSkuWithBarcodeAsync();
        var sourceWarehouse = await Db.CreateWarehouseAsync();
        var destWarehouse = await Db.CreateWarehouseAsync();
        var (sourceLoc, sourceCode) = await Db.CreateLocationAsync(sourceWarehouse, Wms.Modules.Facility.Domain.LocationType.Storage);
        var (receiving, _) = await Db.CreateLocationAsync(destWarehouse, Wms.Modules.Facility.Domain.LocationType.Receiving);

        await using (var inventoryDb = Db.CreateInventoryContext())
        await using (var facilityDb = Db.CreateFacilityContext())
        await using (var masterDb = Db.CreateMasterDataContext())
        {
            var store = new InventoryStore(inventoryDb);
            var opening = new RecordOpeningBalance(store, new MasterDataQueryContract(masterDb), new FacilityQueryContract(facilityDb));
            await opening.Handle(new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, sourceWarehouse, sourceLoc, InventoryStatus.Available, 10), CancellationToken.None);
        }

        await using var bundle = await CreateBundleAsync();

        var transfer = await bundle.CreateTransfer.Handle(
            new CreateTransferCommand(Guid.NewGuid(), null, sourceWarehouse, destWarehouse, null,
                [new CreateTransferLineInput(sku, 5)]),
            CancellationToken.None);
        var allocate = await bundle.AllocateTransfer.Handle(transfer.TransferId, CancellationToken.None);

        var outboundOrder = await bundle.GetOrder.Handle(allocate.OutboundOrderId!.Value, CancellationToken.None);
        foreach (var task in outboundOrder!.PickTasks)
        {
            await bundle.ConfirmPick.Handle(new ConfirmPickCommand(task.Id, sourceCode, barcode, task.RequiredQuantity), CancellationToken.None);
        }

        await bundle.PackOrder.Handle(new PackOrderCommand(allocate.OutboundOrderId.Value, Guid.NewGuid()), CancellationToken.None);

        var ship = await bundle.ShipTransfer.Handle(new ShipTransferCommand(transfer.TransferId), CancellationToken.None);
        var transferDetail = await bundle.GetTransfer.Handle(transfer.TransferId, CancellationToken.None);
        var lineId = transferDetail!.Lines.Single().Id;
        await bundle.ReceiveTransfer.Handle(new Wms.Modules.Transfers.Application.ReceiveTransferCommand(transfer.TransferId, Guid.NewGuid(), lineId, 5, receiving, "AVAILABLE"), CancellationToken.None);

        var consumer = new TransferEventConsumer(bundle.TransferStore, bundle.ShipTransfer, NullLogger<TransferEventConsumer>.Instance);
        var envelope = new IntegrationEventEnvelope(
            ship.InboundReceiptId!.Value,
            IntegrationEventTypes.ReceiptCompleted,
            1,
            DateTime.UtcNow,
            ship.InboundReceiptId.Value,
            JsonSerializer.Serialize(new ReceiptCompletedV1(ship.InboundReceiptId.Value, DateTime.UtcNow, ship.InboundReceiptId.Value, "TRF-IN-X", destWarehouse, ship.InboundReceiptId.Value)));

        var first = await consumer.HandleAsync(envelope, CancellationToken.None);
        var second = await consumer.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal(ConsumerProcessingResult.Ack, first);
        Assert.Equal(ConsumerProcessingResult.Ack, second);

        var final = await bundle.GetTransfer.Handle(transfer.TransferId, CancellationToken.None);
        Assert.Equal(Wms.Modules.Transfers.Domain.TransferStatus.Completed, final!.Status);

        await using var verifyDb = Db.CreateTransfersContext();
        var inbox = await verifyDb.InboxMessages.Where(m => m.EventId == envelope.EventId).ToListAsync();
        Assert.Single(inbox);
    }

    // 19 — Docker RabbitMQ healthcheck çalışıyor (management API).
    [Fact]
    public async Task Rabbitmq_container_healthcheck_reports_ok()
    {
        var (host, _, user, password) = TestConfig.ResolveRabbitMq();
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://{host}:15672/"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

        var response = await client.GetAsync("api/health/checks/alarms");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body, StringComparison.OrdinalIgnoreCase);
    }

    private static IServiceScopeFactory CreateScopeFactory(params Wms.Integration.Outbox.IOutboxStore[] stores)
    {
        var collection = new ServiceCollection();
        foreach (var store in stores)
        {
            collection.AddSingleton(store);
        }

        var provider = collection.BuildServiceProvider();
        return new FakeScopeFactory(provider);
    }

    private sealed class FakePublisher : IRabbitMqPublisher
    {
        public List<OutboxMessage> Published { get; } = [];

        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task DeclareQueueAsync(string queueName, string? routingKey, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RabbitMqStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new RabbitMqStatus(true, "fake"));
    }

    private sealed class FakeScopeFactory(IServiceProvider provider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new FakeScope(provider);
    }

    private sealed class FakeScope(IServiceProvider provider) : IServiceScope
    {
        public IServiceProvider ServiceProvider => provider;

        public void Dispose()
        {
        }
    }

    private sealed record World(Guid Sku, string Barcode, Guid Warehouse, Guid Location, string LocationCode);

    private sealed class Bundle : IAsyncDisposable
    {
        private readonly OutboundDbContext _outboundDb;
        private readonly InboundDbContext _inboundDb;
        private readonly TransfersDbContext _transfersDb;
        private readonly InventoryDbContext _inventoryDb;
        private readonly FacilityDbContext _facilityDb;
        private readonly MasterDataDbContext _masterDb;

        public Bundle(
            OutboundDbContext outboundDb,
            InboundDbContext inboundDb,
            TransfersDbContext transfersDb,
            InventoryDbContext inventoryDb,
            FacilityDbContext facilityDb,
            MasterDataDbContext masterDb,
            OutboundStore outboundStore,
            InboundStore inboundStore,
            TransferStore transferStore,
            InventoryStore inventoryStore,
            IInventoryContract inventoryContract,
            IInboundContract inboundContract,
            IOutboundContract outboundContract,
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
            GetTransfer getTransfer)
        {
            _outboundDb = outboundDb;
            _inboundDb = inboundDb;
            _transfersDb = transfersDb;
            _inventoryDb = inventoryDb;
            _facilityDb = facilityDb;
            _masterDb = masterDb;
            OutboundStore = outboundStore;
            InboundStore = inboundStore;
            TransferStore = transferStore;
            InventoryStore = inventoryStore;
            InventoryContract = inventoryContract;
            InboundContract = inboundContract;
            OutboundContract = outboundContract;
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
        }

        public OutboundStore OutboundStore { get; }

        public InboundStore InboundStore { get; }

        public TransferStore TransferStore { get; }

        public InventoryStore InventoryStore { get; }

        public IInventoryContract InventoryContract { get; }

        public IInboundContract InboundContract { get; }

        public IOutboundContract OutboundContract { get; }

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

        public async ValueTask DisposeAsync()
        {
            await _outboundDb.DisposeAsync();
            await _inboundDb.DisposeAsync();
            await _transfersDb.DisposeAsync();
            await _inventoryDb.DisposeAsync();
            await _facilityDb.DisposeAsync();
            await _masterDb.DisposeAsync();
        }
    }
}
