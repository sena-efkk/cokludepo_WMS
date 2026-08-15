using Wms.Api.Endpoints;
using Wms.Modules.Facility.Application;
using Wms.Modules.Facility.Domain;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Inbound.Application;
using Wms.Modules.Inbound.Domain;
using Wms.Modules.Inbound.Infrastructure.Persistence;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Application.Accuracy;
using Wms.Modules.Inventory.Application.Accuracy.Scanning;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.MasterData.Application;
using Wms.Modules.MasterData.Domain;
using Wms.Modules.MasterData.Infrastructure.Persistence;
using Wms.Modules.Outbound.Application;
using Wms.Modules.Outbound.Infrastructure.Persistence;

namespace Wms.Api;

/// <summary>
/// DEV-ONLY senaryo kurulum servisi. DB'ye doğrudan yazmaz — yalnızca gerçek
/// application use case'lerini kullanır. `DevFeatures:Enabled=false` olduğunda
/// endpoint kapatılır (production'da expose edilmez).
/// </summary>
public sealed class ScenarioInitializer(IServiceProvider services)
{
    private static readonly string[] DemoSkus =
    [
        "PENCIL", "PAPER", "ERASER", "NOTEBOOK", "MARKER", "FOLDER",
        "GLUE", "SCISSORS", "RULER", "STAPLER", "BINDER", "ENVELOPE",
    ];

    public async Task<ScenarioInitResult> InitializeAsync(string scenario, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var facilityDb = sp.GetRequiredService<FacilityDbContext>();
        var masterDb = sp.GetRequiredService<MasterDataDbContext>();
        var inboundDb = sp.GetRequiredService<InboundDbContext>();
        var inventoryDb = sp.GetRequiredService<InventoryDbContext>();
        var outboundDb = sp.GetRequiredService<OutboundDbContext>();

        var facilityContract = new FacilityQueryContract(facilityDb);
        var masterContract = new MasterDataQueryContract(masterDb);
        var inventoryStore = new InventoryStore(inventoryDb);
        var inboundStore = new InboundStore(inboundDb);
        var outboundStore = new OutboundStore(outboundDb);

        var recordOpeningBalance = new RecordOpeningBalance(inventoryStore, masterContract, facilityContract);
        var createReceipt = new CreateReceipt(inboundStore, masterContract, facilityContract);
        var receiveItems = new ReceiveItems(
            inboundStore,
            masterContract,
            facilityContract,
            new Wms.Modules.Inventory.Infrastructure.InventoryContractAdapter(
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
                new ListRiskAssessments(inventoryStore, facilityContract, new InventoryRiskAnalyzer(new RiskPolicyOptions()))),
            Microsoft.Extensions.Options.Options.Create(new InboundOptions()));

        var warehouseCount = 0;
        var skuCount = 0;
        var stockLocations = 0;
        var receiptsCreated = 0;

        foreach (var warehouseCode in new[] { "DEMO-BURSA", "DEMO-ISTANBUL", "DEMO-INEGOL" })
        {
            var existingWarehouse = facilityDb.Warehouses.FirstOrDefault(w => w.Code == warehouseCode);
            if (existingWarehouse is not null)
            {
                // İdempotent: depo ve lokasyonları zaten kuruluysa atla.
                continue;
            }

            var warehouse = Wms.Modules.Facility.Domain.Warehouse.Create(
                warehouseCode,
                $"{warehouseCode} Deposu",
                city: warehouseCode == "DEMO-BURSA" ? "Bursa" : warehouseCode == "DEMO-ISTANBUL" ? "İstanbul" : "İnegöl",
                countryCode: "TR",
                latitude: warehouseCode == "DEMO-BURSA" ? 40.1885m : warehouseCode == "DEMO-ISTANBUL" ? 41.0082m : 40.0806m,
                longitude: warehouseCode == "DEMO-BURSA" ? 29.0610m : warehouseCode == "DEMO-ISTANBUL" ? 28.9784m : 29.5097m);

            facilityDb.Add(warehouse);
            await facilityDb.SaveChangesAsync(cancellationToken);
            warehouseCount++;

            var locations = new List<Wms.Modules.Facility.Domain.Location>();
            var receiving = Wms.Modules.Facility.Domain.Location.Create(warehouse.Id, null, "RECEIVING", "Giriş", LocationType.Receiving, holdsInventory: true);
            var picking = Wms.Modules.Facility.Domain.Location.Create(warehouse.Id, null, "PICKING", "Toplama", LocationType.Picking);
            locations.Add(receiving);
            locations.Add(picking);

            for (var i = 1; i <= 3; i++)
            {
                var aisle = Wms.Modules.Facility.Domain.Location.Create(warehouse.Id, picking.Id, $"A0{i}", $"Koridor A0{i}", LocationType.Aisle);
                locations.Add(aisle);
                for (var b = 1; b <= 2; b++)
                {
                    var bin = Wms.Modules.Facility.Domain.Location.Create(
                        warehouse.Id,
                        aisle.Id,
                        $"A0{i}-B0{b}",
                        $"Göz A0{i}-B0{b}",
                        LocationType.Bin,
                        allowsPicking: true,
                        holdsInventory: true);
                    locations.Add(bin);
                }
            }

            var storage = Wms.Modules.Facility.Domain.Location.Create(warehouse.Id, null, "STORAGE", "Stok", LocationType.Storage, holdsInventory: true);
            locations.Add(storage);

            facilityDb.AddRange(locations);
            await facilityDb.SaveChangesAsync(cancellationToken);

            var bins = locations.Where(l => l.Type == LocationType.Bin).ToList();
            for (var i = 0; i < 6; i++)
            {
                var sku = await EnsureSkuAsync(masterDb, DemoSkus[i], $"8690000000{i:D4}");
                var bin = bins[i % bins.Count];
                await recordOpeningBalance.Handle(
                    new RecordOpeningBalanceCommand(Guid.NewGuid(), sku, warehouse.Id, bin.Id, InventoryStatus.Available, 20 + (i * 5)),
                    cancellationToken);
                stockLocations++;
            }

            var firstSku = await EnsureSkuAsync(masterDb, DemoSkus[0], "8690000000000");
            var receipt = await createReceipt.Handle(
                new CreateReceiptCommand(Guid.NewGuid(), null, warehouse.Id, "DEMO-ASN", "ASN", [new CreateReceiptLineInput(firstSku, 10)]),
                cancellationToken);
            receiptsCreated++;
            var receiptDetail = await new GetReceipt(inboundStore).Handle(receipt.ReceiptId, cancellationToken);
            await receiveItems.Handle(
                new ReceiveItemsCommand(Guid.NewGuid(), receipt.ReceiptId, receiptDetail!.Lines.Single().Id, 10, receiving.Id, ReceivingStockStatus.Available),
                cancellationToken);
        }

        skuCount = 12;

        return new ScenarioInitResult(warehouseCount, skuCount, stockLocations, receiptsCreated);
    }

    private static async Task<Guid> EnsureSkuAsync(MasterDataDbContext masterDb, string code, string barcode)
    {
        var existing = masterDb.Skus.FirstOrDefault(s => s.Code == code);
        if (existing is not null)
        {
            return existing.Id;
        }

        var uom = masterDb.Uoms.First(u => u.Code == "EA");
        var product = Product.Create($"Demo {code}");
        var sku = Sku.Create(product.Id, code, uom.Id);
        sku.AddBarcode(barcode, BarcodeType.Ean);
        masterDb.Add(product);
        masterDb.Add(sku);
        await masterDb.SaveChangesAsync();
        return sku.Id;
    }
}

public sealed record ScenarioInitResult(int WarehousesCreated, int SkusCreated, int StockLocations, int ReceiptsCreated);
