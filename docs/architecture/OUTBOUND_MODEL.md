# Outbound Model (Phase 10)

## Temel Prensip

```text
Outbound owns fulfillment workflow; Inventory owns stock.
A pick failure is evidence, not permission to rewrite stock.
Shipment consumes reserved physical inventory exactly once.
```

Outbound `inventory_balance` / `inventory_ledger` / `inventory_reservation` tablolarına
asla doğrudan yazmaz. Tüm stok etkileşimi Inventory public contract'ı üzerinden:

```text
ReserveOrderAsync          → tüm line'lar tek Inventory transaction'ında (all-or-nothing)
ReportPickNotFoundAsync    → accuracy sinyali (snapshot Inventory'den)
ConsumeReservationAsync    → ship anında fiziksel tüketim (idempotent)
ReleaseReservationAsync    → cancel anında allocation iadesi (idempotent)
GetReservationAsync        → reservation okuma (pick task üretimi, recovery)
```

## Akış

```text
ORDER (CREATED)
  ↓
ALLOCATE → Inventory.ReserveOrder(order.RequestId, lines...)  [all-or-nothing]
  ↓
Inventory Reservation (location-level atomic allocation)
  ↓
Pick Tasks (her reservation line → bir task; UNIQUE(reservation_line_id))
  ├── Confirm (location scan + barcode scan + qty ≤ kalan)
  │     └── PICKED (tüm task'lar COMPLETED)
  │           ↓
  └── NotFound
        ↓
   Inventory Accuracy Signal (PICK_NOT_FOUND + authoritative snapshot)
        ↓
   Risk (RED) / Dynamic Cycle Count (RepeatedNotFound)
        ↓
   Order → PICK_EXCEPTION (stok DEĞİŞMEZ)
        ↓
PACK (PICKED gerekli; stok mutation YOK)
  ↓
SHIP → Inventory.ConsumeReservation (her line)
  ↓
Quantity -= reserved · Allocated -= reserved · Reservation=CONSUMED · Ledger
```

## Order State Makinesi (domain-guarded, arbitrary setter YOK)

```text
CREATED ──allocate──▶ ALLOCATED ──ilk pick──▶ PICKING ──tüm task'lar COMPLETED──▶ PICKED
   │                     │  (veya ALLOCATION_FAILED ──retry──▶ ALLOCATED)
   │                     ├──not found──▶ PICK_EXCEPTION
   │                     └──cancel──▶ CANCELLED (reservation'lar RELEASED)
CREATED ──stok yok──▶ ALLOCATION_FAILED ──retry──▶ ALLOCATED
PICKED ──pack──▶ PACKED ──ship──▶ SHIPPED
PICKING/PICKED/PICK_EXCEPTION/PACKED ──cancel──▶ CANCELLED
SHIPPED ──cancel──▶ YASAK (Return domain)
```

## Allocation (Inventory tek yazar)

- `IInventoryContract.ReserveOrderAsync(requestId, warehouseId, lines, purpose)` — Inventory
  içinde TEK transaction: tüm SKU'ların AVAILABLE balance'ları deadlock-güvenli sırada
  (sku_id artan) FOR UPDATE ile kilitlenir; herhangi bir line karşılanamazsa TÜMÜ rollback
  (dangling reservation imkânsız — testli). Sonuç: `Reserved | InsufficientStock | AlreadyRecorded`.
- Idempotency: per-line reservation RequestId'leri order RequestId'den deterministik türetilir
  (MD5(orderRequestId+skuId)) → retry aynı reservation setini döndürür, `Allocated ×2` imkânsız.
- Outbound kendisi location SEÇMEZ — Inventory `ReservationLine(location, qty)` döndürür.
- `outbound.allocations` tablosu YOK — Outbound yalnız line.ReservationId tutar (testli).

## Picking (location-level)

- PickTask: reservation line başına bir task (order/line/reservation/reservationLine/location/sku/qty).
- Confirm: location scan == task.LocationId (yoksa PICK_LOCATION_MISMATCH), barcode == task.SkuId
  (PICK_SKU_MISMATCH), qty > 0 ve kalanı aşamaz (exceed → 409). Kısmi pick task'ı
  IN_PROGRESS bırakır; `Required 5 / Picked 3 → Completed` YAPILMAZ.
- **Pick confirm ≠ consume**: reservation ship'e kadar korunur; fiziksel tüketim yalnız SHIP'te.

## PickNotFound — Accuracy entegrasyonu

- NotFound → `IInventoryContract.ReportPickNotFoundAsync(requestId, sku, warehouse, location,
  sourceReference=pickTaskId)` → Inventory kendi balance'ından snapshot alır (Outbound quantity
  uydurmaz) → order `PICK_EXCEPTION`. Stok DEĞİŞMEZ; düzeltme yalnız 8.4 reconciliation hattından.
- İki gerçek ardışık NotFound → REPEATED_NOT_FOUND + eski stok → risk RED →
  `EvaluateCycleCountCandidates` gerçek CycleCountTask üretir (testli, gerçek PG).
- Cross-warehouse re-sourcing yok (Phase 14) — sessiz warehouse değişimi YOK.

## Pack & Ship Invariants

```text
Cannot Pack before Picked   (domain + testli)
Cannot Ship before Packed   (domain + testli)
Cannot Ship twice           (UNIQUE(order_id) shipment + idempotent consume)
Cannot Cancel shipped order (domain + testli)
Cannot Consume twice        (Inventory ConsumeReservation idempotent + CONSUMED state)
Pack stok mutation YAPMAZ   (testli)
```

## Failure Recovery (distributed transaction YOK)

- Ship: consume'lar önce (her reservation bağımsız, idempotent), sonra shipment+order tek
  outbound tx'te. Crash sonrası retry: consume no-op → state tamamlanır (testli:
  "Inventory-first crash" simülasyonu).
- Allocate: ReserveOrder AlreadyRecorded + task yok → recovery tamamlar; task var → AlreadyAllocated.
- Duplicate ship (aynı requestId): shipment UNIQUE(order_id)+UNIQUE(request_id) + AlreadyShipped.

## API

```text
POST /api/outbound/orders · GET /api/outbound/orders · GET /api/outbound/orders/{id}
POST /api/outbound/orders/{id}/allocate
GET  /api/outbound/pick-tasks
POST /api/outbound/pick-tasks/{id}/start · /confirm · /not-found
POST /api/outbound/orders/{id}/pack · /ship · /cancel
```

## Database (outbound şeması)

`outbound_fulfillment_order` (UNIQUE request_id + order_number) ·
`outbound_fulfillment_order_line` (UNIQUE(order_id, sku_id)) ·
`outbound_pick_task` (UNIQUE(reservation_line_id) — çifte task imkânsız; CHECK picked <= required) ·
`outbound_package` (UNIQUE(order_id) + UNIQUE(request_id)) ·
`outbound_shipment` (UNIQUE(order_id) + UNIQUE(request_id)).
Cross-module FK yok (DB testiyle korunur).

## Bilinçli Sınırlar (Phase 11+)

Warehouse sourcing/split/resourcing (Phase 14), network inventory, transfer, carrier
entegrasyonu, return/reverse logistics, RabbitMQ/outbox (Phase 13), rota/desi optimizasyonu,
pick confirm idempotency kaydı (mevcut exceed-guard yeterli MVP), frontend.
