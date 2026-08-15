# Fulfillment Sourcing Model (Phase 14)

## Temel Prensip

```text
Sourcing proposes; Inventory reservation commits reality.
```

Fulfillment stok sahibi DEĞİLDİR — yalnızca okur ve plan üretir:

```text
ORDER DEMAND
     ↓
NETWORK INVENTORY (canlı aggregasyon — Phase 11)
     ↓
Candidate Warehouses (yalnız aktif; ATP batch, N+1 yok)
     ↓
Availability + Inventory Reliability + Split Penalty
     ↓
Ranked Plans (açıklanabilir)
     ↓
Commit Sourcing Decision
     ↓
Inventory reservation (Outbound.AllocateOrder → ReserveOrder)
     ↓
Outbound FulfillmentOrder (split → warehouse başına bir order)
```

## Model

- `fulfillment_sourcing_request` (UNIQUE request_id) + `fulfillment_sourcing_line` +
  `fulfillment_sourcing_decision` (UNIQUE request_id + sourcing_request_id, plan snapshot jsonb) +
  `fulfillment_sourcing_order_link` (warehouse → outbound order korelasyonu) — `fulfillment` şeması,
  migration InitialFulfillment. Stok tablosu YOK (testli: fulfillment şemasında stock/balance/inventory
  isimli tablo bulunmaz).

## Candidate Generation (bounded, deterministic)

1. **Single-warehouse**: her aktif warehouse için line bazında `ATP >= requested` kontrolü.
2. **Split**: hiçbir warehouse tek başına complete değilse, coverage'a göre sıralanmış ilk
   `MaxCandidateWarehouses` adayın deterministik 2'li kombinasyonları (default: max 2 warehouse,
   `SourcingOptions.MaxSplitWarehouses`). Brute-force kombinasyon YOK — Phase 15 geliştirir.
3. Karşılanamayan sipariş → `Fulfillable=false` + line bazında explicit shortage
   (Requested / NetworkAtp / Shortage) — sessiz partial fulfillment YOK.

## Scoring (config: `Fulfillment:Sourcing`)

```text
Base 60
+ CompleteFulfillment 25
+ SingleWarehouse 10
- SplitPenalty 20 × (ek warehouse sayısı)
- RiskPenalty: GREEN 0 · YELLOW 8 · ORANGE 16 · RED 30
clamp [0,100]
```

Tie-break (deterministik): Score → FulfillableLineCount → toplam ATP → WarehouseId.

## Inventory Reliability (read-only)

- Risk (`IInventoryContract.ListSkuWarehouseRiskBatchAsync`) yalnız skoru düşürür — **ATP değişmez**
  (testli: RED warehouse ATP=10 olarak kalır, GREEN aynı ATP'yle önce sıralanır).
- `RecentNotFoundCount` açıklamada görünür: "Inventory confidence reduced: N recent PickNotFound signals".
- InTransit (`ITransferContract`) ATP'ye GİRMEZ; yalnız `IncomingStock` context olarak döner (testli).
- Inactive warehouse candidate OLAMAZ (testli).
- HOLD/QUARANTINE/DAMAGED ATP dışı (Phase 11 semantiği — testli).

## Explainability

Her candidate şunları taşır: `Rank, Score, CanFulfillCompletely, FulfillableLineCount,
WorstRiskLevel, RecentNotFoundCount, Explanations[]`:

```text
✓ All 2 order lines available
✓ Single warehouse
✓ ATP sufficient for 2 line(s)
✓ Inventory risk GREEN
✓ In-transit stock excluded from ATP
```

Split planlar için: `Requires 2 shipments — split penalty applied`.

## Commit (reservation zamanı BURASI)

- `CommitSourcingDecision(requestId, sourcingRequestId, plan)`:
  1. Her warehouse için deterministik RequestId'li Outbound order (idempotent) → `AllocateOrderAsync`
     (Inventory ReserveOrder — authoritative concurrency check).
  2. Herhangi bir allocation `InsufficientStock` → **SOURCING_STALE**: oluşan order'lar cancel
     (reservation release), request `Stale` işaretlenir, açık sebep döner → yeniden evaluate edilir.
  3. Başarı → decision + plan snapshot + order linkleri TEK fulfillment transaction'ında.
- **Idempotency**: aynı commit RequestId → `AlreadyCommitted` + mevcut linkler; duplicate
  reservation/order imkânsız (testli: 1 order, 1 reservation).
- **Evaluate stok mutate ETMEZ** (testli: evaluate sonrası allocated=0).

## API

```text
POST /api/fulfillment/sourcing/evaluate       → ranked candidates + shortages + incoming stock
POST /api/fulfillment/sourcing/{id}/commit    → reservation + outbound order(s) | 409 STALE
GET  /api/fulfillment/sourcing/{id}           → request + decision + order linkleri
```

## Bilinçli Sınırlar (Phase 15+)

Google Maps/OSRM, gerçek yol mesafesi, kargo fiyatı/yakıt, rota optimizasyonu, karmaşık split
optimizasyonu, ETA-based InTransit policy, ML, frontend/web simülasyon.
