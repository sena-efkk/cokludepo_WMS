# ADR-0003: Durum Bölmeli InventoryBalance + Append-Only Ledger

- **Tarih:** 2026-08-13 (rev. 2026-08-13 — Validation Gate)
- **Durum:** Accepted

## Context

Stok "ne kadar var" (balance) ve "nasıl geldik" (ledger) sorularını ayırmalı. Aynı zamanda
QUARANTINE/DAMAGED/HOLD gibi durumlar modellenebilmeli; tek `Quantity` alanına
indirgenmemeli. Available değeri için duplicate mutable truth üretilmemeli.

## Decision

1. `inventory_balance` **durum bölmeli**: PK (warehouse_id, location_id, sku_id, status),
   `quantity`, `allocated`. Status: AVAILABLE | HOLD | QUARANTINE | DAMAGED (extensible).
2. **Kavram semantiği (kesin):**
   - AVAILABLE: allocation'a açık **tek** kova.
   - HOLD / QUARANTINE / DAMAGED: sipariş allocation'ına **giremez**.
   - Allocated: **talep sayacı** (durum değil) — "fiziksel stok bir belgeye söz verildi";
     OnHand'i değiştirmez; yalnız AVAILABLE kovasında > 0 olabilir.
   - Available (AvailableToPromise) = `Σ(quantity − allocated)` over **AVAILABLE** —
     **asla saklanmaz**, tek formülle türetilir.
3. **Açık invariant'lar:**
   - Allocation yalnız AVAILABLE kovasından (koşullu UPDATE `status='AVAILABLE'`).
   - `Available` writable truth değildir; tek authoritative kaynak `quantity` ve `allocated` alanlarıdır.
   - Allocation ≠ fiziksel hareket: `Allocate/Deallocate` yalnız sayacı değiştirir; OnHand
     yalnızca fiziksel komutlarla (Receive/Move/Consume/Adjust/TransferOut/TransferIn) değişir.
4. `inventory_transaction` **append-only ledger**: her fiziksel değişim için satır;
   `UNIQUE (reference_type, reference_id, line_no)` idempotency anahtarı; location code
   snapshot'ı taşır (FK'sız dünyada okunabilirlik).
5. Ledger ≠ Event Sourcing: current state tabloları korunur, ledger hareket kaydıdır.
6. DB constraint'ler: `quantity >= 0`, `allocated >= 0`, `allocated <= quantity`,
   `status='AVAILABLE' OR allocated=0`.
7. IN_TRANSIT bilinçli olarak balance'da YOKTUR → Transfers modülünün türetilmiş
   pozisyonudur (`shipped − received − variance`, ADR-0004). ALLOCATED → **Inventory'nin tek
   sahipliğinde**: balance sayacı + `inventory_reservation` kaydı (ADR-0006). Outbound
   allocation state'i yazmaz; kontratla talep eder, reservation id'yi referanslar.

## Alternatives

| Alternatif | Neden reddedildi |
|---|---|
| Tek OnHand/Allocated/Blocked sayaçları | Durum (karantina/hasar) modelleyemez; her yeni durum schema değişikliği |
| Available'ı saklanan kolon yapmak | Duplicate mutable truth; balance değişiminde senkron riski |
| Ledger yerine Event Sourcing | Bu projede gereksiz karmaşıklık; current state yeterli |
| IN_TRANSIT'i inventory kovası yapmak | İki modül tek kavrama yazar; transfer muhasebesi bölünür |

## Consequences

- ✅ Durum geçişleri açık komutlarla yapılır; "hangi durumda ne kadar" her an cevaplanır.
- ✅ Available her zaman doğru: türetilmiş, senkronize edilecek kopya yok.
- ✅ HOLD/QUARANTINE/DAMAGED stok yapısal olarak allocation dışındadır (DB koşulu + test).
- ⚠️ Aggregation sorguları durum kovası sayısıyla büyür → uygun index'ler (`warehouse_id,
  sku_id`), `status` filtreleri; ölçeklenirse warehouse rollup/projeksiyon (ADR-0005).
