# Network Inventory View (Phase 11)

## Temel Prensip

```text
Network inventory is derived, not authoritative.
```

Network View bir **read model / canlı aggregasyon**'dur. Şunların sahibi DEĞİLDİR:
InventoryBalance, InventoryReservation, InventoryLedger, Allocated, Quantity — bunların tek
yazarı Inventory'dir. Network View hiçbir stoğu mutate edemez; kendi writable tablosu YOKTUR.

## Akış

```text
                  Inventory
              authoritative truth
                     │
        ┌────────────┼────────────┐
        ▼            ▼            ▼
      Bursa       İstanbul      İnegöl
        │            │            │
        └────────────┼────────────┘
                     ▼
              Network Inventory
               LIVE AGGREGATION
                     │
             ┌───────┴───────┐
             ▼               ▼
        Network View    Future Sourcing (Phase 14)
```

## Semantics (kesin tanımlar)

```text
NETWORK PHYSICAL STOCK = Σ warehouse physical
                        + future in-transit (Phase 12)

WAREHOUSE PHYSICAL = AVAILABLE + HOLD + QUARANTINE + DAMAGED (location-level toplam)

WAREHOUSE ATP      = Σ AVAILABLE(quantity - allocated)

NETWORK ATP        = Σ warehouse ATP
```

> **Physical ≠ ATP.** Physical, fiziksel varlığı; ATP, satılabilir/atama edilebilir miktarı
> temsil eder. HOLD/QUARANTINE/DAMAGED ATP'ye GİRMEZ.
> **In-transit stock is excluded from ATP in the current policy** (Phase 12'de eklenecek
> InTransit yalnız Physical'a eklenir).

## Yerleşim (Module Placement)

Yeni `Wms.Modules.NetworkInventory` modülü YOK — Phase 2 module map'i source of truth:
Network View, **Fulfillment** modülünde read-only application area olarak yaşar
(ADR-0005: Fulfillment network stok okumanın tüketicisidir). Fulfillment yalnız
`IInventoryContract` + `IFacilityQueryContract` + `IMasterDataQueryContract` kullanır —
Inventory Infrastructure'a dokunmaz (arch testiyle korunur).

## Inventory Contract Yüzeyi (read-only)

```text
GetWarehouseSkuAvailabilityAsync(warehouseId, skuId)
ListSkuWarehouseAvailabilityAsync(skuId)          → sku bazında warehouse rollup'ları
ListSkuLocationBalancesAsync(warehouseId, skuId)  → location+status breakdown (drill-down)
ListWarehouseStockRollupsAsync()                  → warehouse bazında network summary
ListWarehouseSkuRowsAsync(warehouseId, skip, take) → warehouse sku listesi (pagination)
ListSkuWarehousePageAsync(filters, sort, skip, take) → global sku×warehouse sayfası
GetWarehouseSkuRiskAsync / ListSkuWarehouseRiskBatchAsync → read-only risk context
```

Tüm aggregasyonlar DB'de **SQL GROUP BY** ile yapılır (hafızaya satır çekip LINQ Sum YOK).
Risk, mevcut Accuracy analyzer'ından (8.2) okunur — **risk stok miktarını değiştirmez**
(testli): `ATP: 12, RiskLevel: RED` bir arada sunulur; confidence çarpanı YOK (Phase 14 kararı).

## API

```text
GET  /api/network/inventory/skus/{skuId}            → SKU network view (warehouse rollup + risk)
GET  /api/network/inventory/skus?warehouseId=&hasStock=&hasAtp=&riskLevel=&search=&sort=&page=&pageSize=
GET  /api/network/inventory/warehouses/{warehouseId} → warehouse drill-down (sku sayfası + toplamlar)
GET  /api/network/inventory/summary                  → network dashboard rollup
POST /api/network/inventory/availability             → multi-SKU batch (satisfiable + canSatisfy/wh)
```

- Pagination: page/pageSize (default 50, max 200); unbounded list YOK.
- Sort: `atp` / `physical` / `risk`; filtre: warehouseId, hasStock, hasAtp, riskLevel, search
  (SKU kodu / barcode / ürün adı — MasterData contract'ı, Elasticsearch YOK).
- Inactive warehouse stok kaybetmez: `IsOperational=false` ile listelenir (testli).

## Consistency & Performance

Live aggregation → ayrı mutable copy/event lag YOK; Inventory değişimi bir sonraki sorguda
görünür (testli: receive sonrası anında yeni değer). Performans: `GROUP BY warehouse, sku,
status` DB tarafında; mevcut indexler (warehouse+sku, sku, warehouse) kullanılır. Gerçek
ihtiyaç ölçülmeden projection/Redis YOK.

## Future Readiness

- **Phase 12 (Transfers) ✅**: `NetworkPhysicalStock = Σ warehouse + Σ InTransit` uygulandı —
  `ITransferContract.GetOpenInTransitTotalAsync/BySkuAsync` network view'a bağlandı.
  InTransit ATP'ye GİRMEZ (değişmedi). Transfer boyunca network physical sabit (testli).
- **Phase 14 (Sourcing):** `OrderAvailabilityLine` (warehouse ATP + canSatisfy + risk +
  IsOperational) machine-consumable contract'tır — sourcing engine bunu kullanacak.

## Bilinçli Sınırlar (Phase 12+)

InTransit persistence, RabbitMQ/outbox, sourcing algoritması, mesafe/rota hesapları,
cache/Redis, projection worker, frontend dashboard.
