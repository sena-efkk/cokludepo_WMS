# Fulfillment Optimization Model (Phase 15)

## Temel Sınır

```text
Optimization chooses among feasible realities; it does not invent feasibility.
```

Optimizer: stok yaratmaz, değiştirmez, reservation yapmaz, eligibility uydurmaz.
Yalnız Phase 14'ün feasible candidate set'i üzerinde çalışır:

```text
Order
  ↓
Phase 14 Feasible Candidates (hard constraints: aktif warehouse, ATP, coverage, max split)
  ↓
Routes (OSRM → Haversine fallback)
  ↓
Cost Components (9 kalem)
  ↓
Nearest / Greedy / Optimized
  ↓
Ranked Plans + Explainability + Counterfactuals
  ↓
Commit → Inventory Reservation (Phase 14 mekanizması — değişmedi)
```

## Stratejiler (ortak model: candidate/cost/route/decision — üç ayrı sistem YOK)

| Strateji | Davranış |
|---|---|
| `NearestAvailable` | En yakın complete single; yoksa min toplam mesafeli split |
| `GreedyCoverage` | En çok line'ı karşılayandan başlayarak coverage tamamlanana dek ekle (max split sınırlı) |
| `Optimized` | Bounded exhaustive search (max 2 warehouse kombinasyonları, `MaxCandidateWarehouses` sınırlı) — min total cost. OR-Tools bu abstraction arkasına eklenebilir (şu an saf C# solver; native dependency YOK) |

## Cost Model (`FulfillmentCostModel` — tüm katsayılar `Fulfillment:Optimization` config)

```text
TransportCost  = Σ distanceKm × CostPerKm + Σ durationMin × DriverCostPerMinute + shipment × Toll
DispatchCost   = shipment × BaseDispatchCost
PackagingCost  = shipment × PackagingCostPerShipment
HandlingCost   = shipment × HandlingCostPerShipment
PickingCost    = Σ quantity × PickingCostPerUnit
SplitPenalty   = (shipment − 1) × SplitPenaltyCost
Reliability    = Σ line bazında risk penalty (GREEN 0 / YELLOW 1.5 / ORANGE 3.5 / RED 8)
ScarcityPenalty= remainingRatio < 0.2 olan her line için 2.5
SlaPenalty     = 0 (config'de; SLA girdisi Phase 15+)
Total = 9 kalemin toplamı (çift sayım YOK; dispatch ile transport ayrılmıştır)
```

Birim disiplini: km / dakika / decimal para. Binary floating-point YOK — para `decimal`.

## Routing (`IRouteProvider`)

- `OsrmRouteProvider`: self-hosted OSRM HTTP (`Fulfillment:Optimization:OsrmBaseUrl`);
  unavailable/timeout → `RouteUnavailableException`.
- `HaversineRouteProvider`: offline fallback (60 km/saat varsayımı).
- **Fallback sessiz DEĞİL**: plan `RouteSource = HAVERSINE_FALLBACK` taşır.
- `CachingRouteProvider`: deterministik in-memory cache (origin/destination/provider v1) — Redis YOK.
- Warehouse koordinatı eksikse: `ROUTE_DATA_MISSING` (mesafe 0, rastgele (0,0) YOK).

## Solver Güvenliği

- `SolverTimeoutMs` (default 2000) — timeout'ta `Status=TIMEOUT` + `GREEDY_FALLBACK`
  (response bunu AÇIKÇA söyler; kullanıcı optimizer çalıştı sanmaz).
- Hard constraints risk/maliyet ile override edilemez (inactive/ATP yetersiz → aday değil).
- Unfulfilled plan commit edilemez (Phase 14 complete-coverage policy).

## Explainability + Counterfactual

Her plan: `StrategyUsed, Status, ShipmentCount, TotalDistanceKm, CostBreakdown(9 kalem),
RouteSource, Explanations[]` — "Route to customer: 84 km (OSRM)", "Transport 31.40 + Dispatch 8.00…",
"Total 46.90". `compare` ayrıca: `RecommendedStrategy, SavingsVsNearest, Counterfactuals[]`
("Why not NearestAvailable: +8.30 higher total cost").

## Evaluate vs Commit (Phase 14 ayrımı KORUNDU)

- Evaluate/Optimize: SIDE EFFECT YOK.
- Commit: Inventory reservation (Outbound AllocateOrder → ReserveOrder) — stale → `SOURCING_STALE`
  (mevcut mekanizma; optimizer concurrency çözmeye ÇALIŞMAZ).
- Commit artık `OptimizationSnapshot` taşır (strategy/status/cost/route/explanations) —
  decision planSnapshot'ına yazılır: "Bu sipariş neden Bursa'dan çıktı?" cevaplanabilir.

## Eski Repo İlişkisi (warehouse-route-optimizer)

> Reference/benchmark only; not authoritative inventory or runtime dependency.

Kavramsal adapte edilenler: candidate_selector/coverage/plan_generator/plan_explainer/
baseline strategy karşılaştırması. TAŞINMAYANLAR: CSV inventory, Python mutable stock,
simulation consumption — source of truth NetworkInventory + Reservation'dır. Repo lokal
değildi → deterministik regression fixture (`Benchmark_fixture_matches_documented_baseline`,
sabit koordinat/katsayılarla elle doğrulanmış baseline) eklendi; repo klonlandığında aynı
senaryo Python tarafıyla karşılaştırılabilir.

## API (mevcut sourcing API genişletildi — duplicate API YOK)

```text
POST /api/fulfillment/sourcing/evaluate
  { destination?, destinationLatitude?, destinationLongitude?, strategy?: nearest|greedy|optimized|compare, lines[] }
  → candidates + optimization (tek plan) veya comparison (3 plan + öneri + savings + counterfactuals)
POST /api/fulfillment/sourcing/{id}/commit
  { plan, optimization? (snapshot) } → reservation + outbound order(s) | 409 STALE
```

## Bilinçli Sınırlar (Phase 16+)

Gerçek Türkiye OSRM map import'u (CI/test zorunlu DEĞİL — fake provider testlerde),
carrier API/fiyat, SLA/ETA policy, ML forecasting, OR-Tools native solver, GoogleRouteProvider,
frontend/grafana/prometheus.
