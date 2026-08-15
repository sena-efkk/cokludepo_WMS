# Inbound Model (Phase 9)

## Temel Prensip

```text
Inbound owns the workflow; Inventory owns stock mutation.
A successful receipt must leave an Inventory ledger trail.
Putaway must reuse the existing scan-enforced movement path.
Retry must never duplicate physical stock.
```

Inbound kendi SQL'iyle `inventory.inventory_balance` / `inventory.inventory_ledger`
tablolarına **asla** yazmaz. Tüm stok değişimi Inventory public contract'ı üzerinden:

```text
IInventoryContract.ReceiveInventoryAsync(...)          → RECEIVED ledger + balance (atomic)
IInventoryContract.ExecuteScannedRelocationAsync(...)  → Phase 8.5 motoru (movement + ledger + scan evidence)
```

## Akış

```text
EXPECTED RECEIPT (InboundReceipt, OPEN)
   SKU-X expected 10
        ↓
Physical Receiving (truck deliveries — partial destekli)
        ↓
Receive 8 AVAILABLE  → ReceiptLineReceiveRecord (append-only)
Receive 2 DAMAGED    → Inventory.ReceiveInventory (balance + RECEIVED ledger)
        ↓
Receiving/Staging Location balance'ı
        ↓
PutawayTask (her receive record → TEK task; UNIQUE(receive_record_id))
        ↓
RF Scan Validation (source == task source, barcode == task SKU)
        ↓
ExecuteScannedRelocation (Phase 8.5 — movement + RELOCATED_OUT/IN + scan evidence)
        ↓
Final Location + receipt COMPLETED (tüm task'lar bitince)
```

## Varlıklar (inbound şeması)

| Entity | Amaç | Kritik kısıtlar |
|---|---|---|
| `inbound_receipt` | Expected receipt (PO/ASN'den bağımsız) | UNIQUE request_id, UNIQUE receipt_number; explicit status transition'ları |
| `inbound_receipt_line` | SKU bazında beklenen/gelen | expected >= 0, received >= 0 (CHECK); UNIQUE(receipt_id, sku_id) |
| `inbound_receipt_record` | Fiziksel receive op'larının append-only izi | UNIQUE request_id; disposition + receiving location + inventory operation id |
| `inbound_putaway_task` | RCV/STAGING'den final yere taşıma görevi | UNIQUE receive_record_id (çifte putaway imkânsız); quantity > 0 |

**Cross-module FK yok:** SkuId/WarehouseId/LocationId/InventoryOperationId yalnızca Guid
(contract'lar üzerinden doğrulanır). Module-local FK'ler (receipt→line→record) serbest.
Architecture testi bunu DB'de doğrular (information_schema FK kontrolü).

## Receipt Status Makinesi (explicit, domain-guarded)

```text
OPEN ──(ilk kısmi receive)──▶ PARTIALLY_RECEIVED
OPEN ──(tüm line'lar tam)──▶ RECEIVED
PARTIALLY_RECEIVED ──(tüm line'lar tam)──▶ RECEIVED
RECEIVED ──(putaway başladı)──▶ PUTAWAY_IN_PROGRESS
PUTAWAY_IN_PROGRESS ──(tüm task'lar COMPLETED)──▶ COMPLETED
RECEIVED ──(tüm task'lar COMPLETED, start atlanmışsa)──▶ COMPLETED
OPEN ──(cancel; hiç fiziksel receive YOK)──▶ CANCELLED
```

- `ReceivedQuantity` yalnız receiving operation sonucu artar — arbitrary setter YOK.
- Fiziksel receive sonrası cancel YASAK: stok sisteme girmiştir, düzeltme explicit
  inventory operation (reconciliation/adjustment) gerektirir — stok asla sihirle silinmez.

## Discrepancy (Short / Over / Damaged)

- Her receive record için disposition hesaplanır: `== expected → MATCHED`,
  `> expected → OVER`, `< expected → SHORT` (son durumu yansıtır; sonraki teslimat MATCHED'e çevirebilir).
- **Over policy:** `Inbound:AllowOverReceipt` (default `false` — strict). Kapalıyken
  expected'ı aşan receive 409 ile reddedilir; açıkken OVER disposition ile kaydedilir.
  Supplier claims/accounting bu fazın kapsamı dışında.
- **Damaged/Quarantine:** receive komutu Inventory status partition'ını seçer
  (AVAILABLE/HOLD/QUARANTINE/DAMAGED) — yeni inbound-specific status YOK.
  QUARANTINE/DAMAGED balance allocation'a kapalıdır (Inventory CHECK + Reserve davranışı).

## Receiving Location Doğrulaması

Receive yalnız şu lokasyonlara yapılır: mevcut, aktif, receipt'in warehouse'ına ait,
`HoldsInventory=true` ve tipi `RECEIVING` veya `STAGING`. Aksi → `InvalidReceivingLocationException`
(400). Ürün asla doğrudan final storage'a "ışınlanmaz" — fiziksel iz RCV/STAGING'den geçer.

## Putaway Orchestration

```text
CompletePutaway(taskId, requestId, sourceScan, skuScan, destinationScan, quantity, deviceId, operatorId)
  1. quantity == task.Quantity (kısmi putaway bu fazda yok)
  2. source scan → task.SourceLocationId eşleşmesi (PUTAWAY_SOURCE_MISMATCH — sessiz source değişimi yok)
  3. barcode → task.SkuId eşleşmesi (PUTAWAY_SKU_MISMATCH)
  4. IInventoryContract.ExecuteScannedRelocationAsync(...)  → Phase 8.5:
     - destination active/same-warehouse/storage-capable validasyonu (WrongWarehouse,
       DestinationNotAllowed, LocationInactive, DestinationNotFound)
     - AVAILABLE stock row-lock + RELOCATED_OUT/IN ledger + scan evidence — tek transaction
  5. Rejected → görev PENDING kalır, kod+reason döner; Completed → task COMPLETED + movement_id
  6. Receipt: tüm task'lar COMPLETED ise → COMPLETED (yoksa PUTAWAY_IN_PROGRESS)
```

- Non-AVAILABLE putaway (DAMAGED/QUARANTINE/HOLD) bilinçli olarak ertelendi:
  `PUTAWAY_STATUS_NOT_SUPPORTED` — status-change hareketi ayrı bir işlem (Phase 10+).
- Akıllı "en iyi raf" önerisi YOK — operatör destination tarar (Optimization alanı).

## Recovery & Idempotency (cross-module, distributed transaction YOK)

```text
ReceiveItems(RequestId)
  ├─ Inbound: record var mı? (UNIQUE request_id) → varsa AlreadyRecorded
  ├─ Inventory.ReceiveInventory(RequestId)
  │     └─ inventory_operation PK: ilk çalıştırmada balance+ledger, sonrakilerde DuplicateRequest
  ├─ Inbound: line satır kilidi (FOR UPDATE) altında re-check → record + task + status (tek tx)
  └─ Crash sonrası retry: Inventory "Duplicate" der → Inbound eksik kaydını güvenle yazar,
     stok TEK kez artmıştır (testli: Inventory-first crash simülasyonu).
```

Putaway recovery: movement RequestId idempotency (Phase 8.5) → retry aynı movement'a
bağlanır, task completion idempotent'tir, duplicate movement imkânsızdır (testli).

## Inventory Tarafında Eklenenler

- `LedgerEntryType.Received` + ledger'a `reference_type`/`reference_id` (INBOUND_RECEIPT + receiptId
  ile tam izlenebilirlik; migration AddLedgerReferenceColumns).
- `ReceiveInventory` use case + `ExecuteReceiveAsync` (operation row + balance upsert +
  ledger — tek transaction).
- `IInventoryContract.ReceiveInventoryAsync` + `ExecuteScannedRelocationAsync`
  (Phase 8.5'in kontrat yüzeyi — Inbound Application'a değil, yalnızca Contracts'a bakar).

## API

```text
POST /api/inbound/receipts
GET  /api/inbound/receipts · GET /api/inbound/receipts/{id}
POST /api/inbound/receipts/{id}/receive
POST /api/inbound/receipts/{id}/cancel
GET  /api/inbound/putaway-tasks · GET /api/inbound/putaway-tasks/{id}
POST /api/inbound/putaway-tasks/{id}/start
POST /api/inbound/putaway-tasks/{id}/complete
```

## Accuracy Bağlantısı (Phase 9'da büyük model YOK)

Receive discrepancy'ler (SHORT/OVER/damaged), putaway ret'leri ve source/sku mismatch'leri
append-only kayıtlarda korunur (receive record disposition + task rejection reason) — ileride
Inventory Accuracy sinyallerine beslenebilecek evidence altyapısı hazır.

## Bilinçli Sınırlar (Phase 10+)

Purchase Order/ASN modülü, supplier management, QC workflow, kısmi putaway,
non-AVAILABLE putaway (status-change move), akıllı putaway önerisi, outbox/broker.
