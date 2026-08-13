# ADR-0002: Generic Hiyerarşik Location Modeli

- **Tarih:** 2026-08-13
- **Durum:** Accepted

## Context

Her deponun fiziksel topolojisi farklı olabilir (Bursa: Zone→Aisle→Rack→Level→Bin;
İstanbul: Floor/Pallet/Cold storage; İnegöl: Reserve/Picking/Cross Dock). Sistem
"her depoda aynı koridor/raf/bin yapısı var" varsayımı yapmamalı; yeni depo kurulumu
kod deploy gerektirmemeli.

## Decision

`Warehouse → Location (self-referencing)` genel ağacı:

- `Location.ParentLocationId` → aynı warehouse'daki üst location (kök = warehouse).
- `LocationType` enum: ZONE, AISLE, RACK, LEVEL, BIN, FLOOR, DOCK, RECEIVING, QC,
  RESERVE, PICKING, PACKING, STAGING, SHIPPING, QUARANTINE, DAMAGED, RETURNS, CROSS_DOCK.
- Kararlar capability flag'lerinden okunur (`AllowsPicking`, `AllowsPutaway`,
  `HoldsInventory`, fiziksel limitler) — **`Location.Code` parse edilmez**, Type string'i
  business kural kaynağı değildir.
- Kurallar: (WarehouseId, Code) UNIQUE; parent aynı warehouse'da; cycle yasak;
  stok tutan location leaf olmalı (HoldsInventory); hard delete yok (deactivate).

## Alternatives

| Alternatif | Neden reddedildi |
|---|---|
| Rigid `Warehouse→Zone→Aisle→Rack→Level→Bin` sabit şema | Farklı depo topolojilerini temsil edemez; yeni seviye = schema + kod değişimi |
| `Location.Code` string'ini parse ederek hiyerarşi çıkarmak | Kırılgan, insan okur kodu domain kuralı yapmak anti-pattern |
| Her LocationType için ayrı entity (RackTable, BinTable...) | Tip sayısı kadar tablo; ortak mantık tekrarlanır |

## Consequences

- ✅ Her depo tipi admin UI ile kurulabilir, kod değişmez.
- ✅ UI'da genel ağaç editörü tüm depo tiplerini destekler (Location tree UX).
- ⚠️ "Derinlik/disiplin" DB seviyesinde zorlanamaz → domain validasyonları + testler gerekli
  (cycle, parent-warehouse, leaf-stock).
- ⚠️ Type enum'u büyüyebilir → yalnızca gerçek semantik ayrım gerektiğinde yeni tip eklenir.
