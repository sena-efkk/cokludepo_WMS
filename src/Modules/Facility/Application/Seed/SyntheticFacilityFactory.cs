using Wms.Modules.Facility.Domain;

namespace Wms.Modules.Facility.Application.Seed;

public sealed record FacilitySeedItem(
    string Code,
    string Name,
    LocationType Type,
    string? ParentCode = null,
    bool AllowsPicking = false,
    bool AllowsPutaway = false,
    bool AllowsReplenishment = false,
    bool HoldsInventory = false);

public sealed record FacilitySeedPlan(
    string WarehouseCode,
    string WarehouseName,
    string City,
    decimal Latitude,
    decimal Longitude,
    IReadOnlyList<FacilitySeedItem> Locations);

public sealed record FacilitySeedResult(int WarehousesCreated, int LocationsCreated, int Skipped);

public static class SyntheticFacilityFactory
{
    public static IReadOnlyList<FacilitySeedPlan> CreatePlans() =>
    [
        new FacilitySeedPlan(
            "BURSA-01",
            "Bursa Deposu",
            "Bursa",
            40.1885m,
            29.0610m,
            [
                new("RECEIVING", "Giriş Alanı", LocationType.Receiving, AllowsPutaway: true, HoldsInventory: true),
                new("QC", "Kalite Kontrol", LocationType.Qc),
                new("PICKING", "Toplama Bölgesi", LocationType.Picking, AllowsPicking: true),
                new("A01", "Koridor A01", LocationType.Aisle, ParentCode: "PICKING", AllowsPicking: true),
                new("A01-R01", "Raf R01", LocationType.Rack, ParentCode: "A01", AllowsPicking: true, HoldsInventory: true),
                new("A01-R01-L01", "Seviye L01", LocationType.Level, ParentCode: "A01-R01", HoldsInventory: true),
                new("A01-R01-L01-B01", "Göz B01", LocationType.Bin, ParentCode: "A01-R01-L01", AllowsPicking: true, HoldsInventory: true),
                new("A01-R01-L01-B02", "Göz B02", LocationType.Bin, ParentCode: "A01-R01-L01", AllowsPicking: true, HoldsInventory: true),
                new("A01-R02", "Raf R02", LocationType.Rack, ParentCode: "A01", AllowsPicking: true, HoldsInventory: true),
                new("A02", "Koridor A02", LocationType.Aisle, ParentCode: "PICKING", AllowsPicking: true),
                new("A02-R01", "Raf R01", LocationType.Rack, ParentCode: "A02", AllowsPicking: true, HoldsInventory: true),
                new("A02-R01-L01-B01", "Göz B01", LocationType.Bin, ParentCode: "A02-R01", AllowsPicking: true, HoldsInventory: true),
                new("A02-R01-L01-B02", "Göz B02", LocationType.Bin, ParentCode: "A02-R01", AllowsPicking: true, HoldsInventory: true),
                new("BULK-STORAGE", "Yedek Stok Alanı", LocationType.Storage, AllowsPutaway: true, AllowsReplenishment: true, HoldsInventory: true),
                new("PACKING", "Paketleme Alanı", LocationType.Packing),
                new("STAGING", "Sevkiyat Hazırlık", LocationType.Staging, HoldsInventory: true),
                new("SHIPPING", "Sevkiyat Bölgesi", LocationType.Shipping),
                new("DOOR-1", "Kapı 1", LocationType.Dock, ParentCode: "SHIPPING"),
                new("DOOR-2", "Kapı 2", LocationType.Dock, ParentCode: "SHIPPING"),
                new("RETURNS", "İade Alanı", LocationType.Returns),
            ]),
        new FacilitySeedPlan(
            "IST-01",
            "İstanbul Deposu",
            "İstanbul",
            41.0082m,
            28.9784m,
            [
                new("FLOOR-1", "Kat 1", LocationType.Floor, HoldsInventory: true),
                new("SECTION-A", "Bölüm A", LocationType.Zone, ParentCode: "FLOOR-1", AllowsPicking: true, HoldsInventory: true),
                new("POS-01", "Pozisyon 01", LocationType.Bin, ParentCode: "SECTION-A", AllowsPicking: true, HoldsInventory: true),
                new("POS-02", "Pozisyon 02", LocationType.Bin, ParentCode: "SECTION-A", AllowsPicking: true, HoldsInventory: true),
                new("POS-03", "Pozisyon 03", LocationType.Bin, ParentCode: "SECTION-A", AllowsPicking: true, HoldsInventory: true),
                new("SECTION-B", "Bölüm B", LocationType.Zone, ParentCode: "FLOOR-1", AllowsPicking: true, HoldsInventory: true),
                new("POS-04", "Pozisyon 04", LocationType.Bin, ParentCode: "SECTION-B", AllowsPicking: true, HoldsInventory: true),
                new("POS-05", "Pozisyon 05", LocationType.Bin, ParentCode: "SECTION-B", AllowsPicking: true, HoldsInventory: true),
                new("FLOOR-2", "Kat 2", LocationType.Floor),
                new("COLD-STORAGE", "Soğuk Depo Bölgesi", LocationType.Zone, ParentCode: "FLOOR-2", AllowsPutaway: true, HoldsInventory: true),
                new("C-01", "Soğuk Göz 01", LocationType.Bin, ParentCode: "COLD-STORAGE", AllowsPutaway: true, HoldsInventory: true),
                new("C-02", "Soğuk Göz 02", LocationType.Bin, ParentCode: "COLD-STORAGE", AllowsPutaway: true, HoldsInventory: true),
                new("RECEIVING", "Giriş Alanı", LocationType.Receiving, AllowsPutaway: true, HoldsInventory: true),
                new("SHIPPING", "Sevkiyat Bölgesi", LocationType.Shipping),
                new("DOOR-1", "Kapı 1", LocationType.Dock, ParentCode: "SHIPPING"),
            ]),
        new FacilitySeedPlan(
            "INEGOL-01",
            "İnegöl Deposu",
            "İnegöl",
            40.0806m,
            29.5097m,
            [
                new("RESERVE", "Rezerv Alanı", LocationType.Reserve, AllowsPutaway: true, HoldsInventory: true),
                new("RES-R01", "Rezerv Rafı R01", LocationType.Rack, ParentCode: "RESERVE", AllowsPutaway: true, HoldsInventory: true),
                new("RES-R01-L01-B01", "Rezerv Göz B01", LocationType.Bin, ParentCode: "RES-R01", AllowsPutaway: true, HoldsInventory: true),
                new("RES-R01-L01-B02", "Rezerv Göz B02", LocationType.Bin, ParentCode: "RES-R01", AllowsPutaway: true, HoldsInventory: true),
                new("PICKING", "Toplama Bölgesi", LocationType.Picking, AllowsPicking: true),
                new("FLOW-01", "Akış Rafı 01", LocationType.Rack, ParentCode: "PICKING", AllowsPicking: true, HoldsInventory: true),
                new("FLOW-01-B01", "Göz B01", LocationType.Bin, ParentCode: "FLOW-01", AllowsPicking: true, HoldsInventory: true),
                new("FLOW-01-B02", "Göz B02", LocationType.Bin, ParentCode: "FLOW-01", AllowsPicking: true, HoldsInventory: true),
                new("FLOW-01-B03", "Göz B03", LocationType.Bin, ParentCode: "FLOW-01", AllowsPicking: true, HoldsInventory: true),
                new("CROSS-DOCK", "Cross Dock Alanı", LocationType.CrossDock),
                new("SHIPPING", "Sevkiyat Bölgesi", LocationType.Shipping),
                new("DOOR-1", "Kapı 1", LocationType.Dock, ParentCode: "SHIPPING"),
                new("DOOR-2", "Kapı 2", LocationType.Dock, ParentCode: "SHIPPING"),
            ]),
    ];
}
