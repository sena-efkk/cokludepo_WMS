# Data Ownership & Source of Truth

> Her verinin **tek sahibi** ve **tek yazılabilir gerçeği (source of truth)** vardır.
> Diğer tüm görünümler ya sahibinden okunur, ya sahibinin ürettiği kontrat/event'ten türetilir.
> Bu tablo ihlal edilirse stok tutarsızlığı, çift yazarlı truth ve silo'lara gömülü mantık kaçınılmazdır.

## Domain Ownership

| Kavram | Sahip Modül | Not |
|---|---|---|
| Product, SKU, Barcode, UOM, Category, Brand, boyut/ağırlık, saklama gereksinimi | **MasterData** | Fiziksel stok tutmaz |
| Warehouse, Zone, Location, LocationCapability, Dock | **Facility** | Stok miktarı tutmaz |
| InventoryBalance, InventoryTransaction (ledger), stok durumu, **Allocated sayacı**, **InventoryReservation (allocation state + reservation lifecycle)** | **Inventory** | Stok invariant'larının ve allocation state'inin **tek sahibi** (ADR-0006) |
| Receipt, Receiving, QC, PutawayTask | **Inbound** | |
| FulfillmentOrder (yürütme tarafı), PickTask, Pack, Shipment | **Outbound** | Allocation state'i YAZMAZ; Inventory kontratıyla talep eder, reservation id'yi yalnızca referanslar (ADR-0006) |
| TransferOrder, TransferLine, InTransit pozisyonu | **Transfers** | InTransit türetilmiştir: `shipped − received − variance` |
| Sourcing kararı, FulfillmentDecision, aday depo skorlama | **Fulfillment** | Stok yazmaz; karar açıklanabilir olmalı |
| User, Role, UserWarehouseAccess, AuditLog | **Administration** | AuditLog ≠ stok ledger'ı (ADR-0007) |
| Dış sistem adaptörleri, outbox relay/inbox transport | **Integration** *(teknik)* | Business bounded context değildir; event kontratları business modüllerindir |

## Source of Truth Matrix

**Tür sütunu:** `Writable` = tek yazılabilir gerçek · `Derived` = asla yazılmaz, türetilir.
Bu matriste **joint/iki yazarlı truth YOKTUR** — her satırın tek sahibi vardır.

| Veri | Sahip | Source of Truth | Scope | Tür | Türetme / Not |
|---|---|---|---|---|---|
| Product | MasterData | `master_data.product` | Company | Writable | — |
| SKU / Barcode / UOM | MasterData | `master_data.sku` vb. | Company | Writable | — |
| Warehouse | Facility | `facility.warehouse` | Company | Writable | — |
| Location + capabilities | Facility | `facility.location` | Warehouse | Writable | hard delete yok; deactivate/retire |
| **Location Inventory Balance** | Inventory | `inventory.inventory_balance` | Warehouse | Writable | PK: (warehouse, location, sku, status) |
| **Inventory Ledger** | Inventory | `inventory.inventory_transaction` | Warehouse | Writable (append-only) | her balance değişiminde satır |
| **Allocated sayacı** | Inventory | `inventory_balance.allocated` | Warehouse | Writable | tek yazar Inventory; reservation kaydıyla aynı tx (ADR-0006) |
| **InventoryReservation / Allocation state** | Inventory | `inventory.inventory_reservation` | Warehouse | Writable | lifecycle: ALLOCATED → CONSUMED / RELEASED; Outbound yalnızca **reservation id referanslar** |
| **Available (ATP)** | — (formül) | türetilmiş: `Σ_AVAILABLE(quantity − allocated)` | W/H/N | **Derived** | writable kolon hiçbir yerde YOK |
| **Warehouse Inventory Summary** | Inventory (read model) | location balance'lardan canlı SUM | Warehouse | **Derived** | saklanmaz |
| **Network Physical Stock** | (read model) | `Σ warehouse OnHand + Σ InTransit` | Network | **Derived** | fiziksel ağ görünümü; saklanmaz (ADR-0005) |
| **Network ATP** | (read model) | `Σ warehouse Available` — **InTransit DAHİL DEĞİL (MVP)** | Network | **Derived** | InTransit'in ATP'ye katılımı ileride business policy (ADR-0005) |
| Receipt / PutawayTask | Inbound | `inbound.*` | Warehouse | Writable | — |
| FulfillmentOrder / Pick / Pack / Shipment | Outbound | `outbound.*` | Warehouse | Writable | reservation id = referans, truth değil |
| TransferOrder / TransferLine | Transfers | `transfers.*` | Network | Writable | satır miktarları workflow ilerlemesidir |
| **InTransit quantity** | Transfers | türetilmiş: `shipped − received − variance` | Network | **Derived** | ayrı tablo/kolon YOK |
| FulfillmentDecision | Fulfillment | `fulfillment.fulfillment_decision` | Network | Writable | stok rezervasyonu değildir |
| User | Administration | `administration.app_user` | Company | Writable | — |
| Warehouse Access | Administration | `administration.user_warehouse_access` | Company | Writable | UNIQUE (user, warehouse) |
| Outbox / Inbox | her modül kendisininkini sahiplenir | `<module>.outbox` | — | Writable (altyapı) | relay Integration'da çalışır |

## Çift Yazılabilir Truth YASAKLARI

Aşağıdaki "kolaylık" alanları/tabloları **bilinçli olarak ÜRETİLMEZ**. Hepsi ya sahibinden
okunur ya da sorgu anında türetilir:

- ❌ `sku.total_stock` — MasterData'da stok toplamı kolonu.
- ❌ `warehouse.total_stock` — Facility'de stok toplamı kolonu.
- ❌ `location.quantity` — Location üzerinde stok miktarı.
- ❌ `inventory_balance.available` — Available saklanan kolon; **her zaman hesaplanır**:
  `Available = Σ(STATUS=AVAILABLE) (qty − allocated)` (bkz. [INVENTORY_MODEL.md](INVENTORY_MODEL.md)).
- ❌ `outbound.allocation` / Outbound tarafında allocation state tablosu — allocation state'inin
  writable truth'u Inventory'nin `inventory_reservation`'ıdır; Outbound yalnızca reservation id saklar.
- ❌ `transfer_line.in_transit_qty` — saklanan InTransit; `shipped − received − variance` türetilir.
- ❌ `transfer.available_before/after` gibi denormalize stok kopyaları.

Kabul edilen türetilmiş state'ler (sahipleri tarafından, kendi transaction'ında güncellenir):

- `inventory_balance.allocated` + `inventory.inventory_reservation` — Inventory'nin **tek yazar**
  olduğu sayaç + kayıt ikilisi; ikisi Inventory'nin aynı transaction'ında değişir (ADR-0006).
- `transfer_line.shipped_qty / received_qty / variance_qty` — transfer workflow'unun kendi
  ilerleme durumu (allocation state'inin kopyası değildir).

## Kapsam (Scope) Sınıflandırması

```text
COMPANY scope  : tüm depolarda ortak, depo bilmez   → Product, SKU, UOM, User, Warehouse
WAREHOUSE scope: bir depoya ait, WarehouseId taşır   → Location, Balance, Reservation, Receipt, PickTask...
NETWORK scope  : depolar ÜSTÜ görünüm                → TransferOrder, FulfillmentDecision, Network görünümleri
```

Warehouse-scope her aggregate'da `WarehouseId` bulunur ve **her sorgu bu filtreyle yürür**
(Authorization katmanı tarafından zorunlu tutulur — bkz. [ACCESS_CONTROL.md](ACCESS_CONTROL.md)).
