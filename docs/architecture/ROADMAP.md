# Roadmap

> İlk hedef: **Modular Monolith** (ASP.NET Core + PostgreSQL, tek deployment).
> Her faz küçük, anlamlı increment'lere bölünür; her increment sonunda sistem **çalışır durumda** kalır.
> Fazlar ardışık değildir — ör. UI, ilgili domain fazına paralel gelişir.
>
> **Faz numaralandırması bu belgeyle standardize edilmiştir** ve tüm dokümantasyonda
> tek referanstır. Önceki numaralandırma (Discovery=0, Foundation=1, ...) kullanımdan kalkmıştır.

## Faz Özeti

| # | Faz | Kapsam | Çıkış Kriteri (özet) |
|---|---|---|---|
| 0 | **Problem Definition** | Proje hedefi, gereksinimler, öğrenme amaçları | MASTER PROJECT CONTEXT ✅ |
| 1 | **Repository Bootstrap** | Klasör iskeleti, docs yapısı | `src/`, `tests/`, `deploy/`, `docs/` ✅ |
| 2 | **Architecture Discovery & Design** | Sistem bağlamı, modül haritası, veri sahipliği, tutarlılık, ADR'ler | Bu belge seti + ADR-0001..0007 ✅ (mevcut faz — Validation Gate tamamlandı) |
| 3 | **.NET Solution Foundation** | Çözüm, modül projeleri, API kabuğu, arch testleri | Modül bağımlılık testleri geçer; API ayakta ✅ (kısmen — bkz. increment tablosu) |
| 4 | **PostgreSQL / Infrastructure Foundation** | Compose + şemalar + tek connection + health + persistence testleri | Bağlantı doğrulanır; 8 şema hazır ✅ (DbContext'ler Phase 5) |
| 5 | **MasterData** | SKU/Product/Barcode/UOM CRUD + arama | SKU oluşturulabilir/aranabilir ✅ |
| 6 | **Facility** | Warehouse + hiyerarşik Location + capability'ler | A ve B deposu tamamen farklı topolojiyle kurulabilir ✅ |
| 7 | **Inventory Core** | Balance (durum bölmeli) + ledger + receive/adjust + status | Negatif stok imkânsız; ledger her hareketi kaydeder ✅ |
| 8 | **Internal Movement** | Location→Location move | Warehouse-total invariant testi geçer ✅ |
| 9 | **Inbound** | InboundReceipt → Receiving → Putaway (scan-enforced) | Tedarikçi girişi uçtan uca çalışır; retry stok kopyalamaz ✅ |
| 10 | **Outbound** | FulfillmentOrder → Allocation → Pick → Pack → Ship | Concurrency acceptance testi geçer (Available=1, iki sipariş) ✅ |
| 11 | **Network View** | SKU drill-down: Network → Warehouse → Location | SKU araması üç seviyeyi gösterir ✅ |
| 12 | **Transfers** | TransferOrder state machine + InTransit muhasebesi | Multi-warehouse acceptance senaryosu geçer ✅ |
| 13 | **Event Integration** | RabbitMQ + Outbox/Inbox (gerçek ihtiyaçla) | Duplicate message zararsız; outbox atomik ✅ |
| 14 | **Fulfillment Sourcing** | Aday depo skorlama (deterministic) + açıklanabilir sonuç | Karar: depo + skor + neden listesi ✅ |
| 15 | **Optimization** | Putaway strategy → slotting → picking route (tek tek) | Strateji abstraction'ı değiştirilebilir ✅ (sourcing cost/route optimizasyonu) |
| 16 | **Observability** | OpenTelemetry + Prometheus + Grafana + structured logs | Operasyonlar ve anomaliler izlenebilir ✅ |
| 17 | **Web UI & Demo** | React SPA (tüm akışlar) + Docker demo dağıtımı + Playwright E2E | `docker compose up -d --build` ile demo; 6 E2E senaryo geçer ✅ |

## Increment Detayı

### Phase 0 — Problem Definition ✅

MASTER PROJECT CONTEXT: hedef, gereksinimler, anti-pattern listesi, öğrenme amaçları.

### Phase 1 — Repository Bootstrap ✅

Klasör iskeleti (`src/`, `tests/`, `deploy/`, `docs/`) ve doküman stub'ları oluşturuldu.

### Phase 2 — Architecture Discovery & Design ✅

Mimari tasarım seti (`docs/architecture/*`), ADR-0001..0007, Validation Gate revizyonları.
**Kod yazılmadı.** Onaylandıktan sonra Phase 3 başlar.

### Phase 3 — .NET Solution Foundation

| Increment | İçerik | Durum |
|---|---|---|
| 3.1 | Solution + 8 modül projesi + `Wms.Api` + architecture testleri (Mono.Cecil — ADR-0008) | ✅ tamamlandı |
| 3.2 | API kabuğu detayları: standart error modeli, CorrelationId, structured logging pipeline | ⏳ Phase 4+ (bu fazda yalnız root + /health) |
| 3.3 | Administration: Identity + UserWarehouseAccess + authorization policy'leri (ADR-0007) | ⏳ ilgili fazda (bu fazda bilinçli olarak yasaktı) |

**Önemli:** `SharedKernel` dev çöplük olmaz; yalnızca gerçek cross-cutting primitifler
(ör. entity base, domain event base, error tipleri) küçük bir package'ta yaşar.
Generic `IRepository<T>` YOK; persistence abstraction'ları use-case odaklıdır.

### Phase 4 — PostgreSQL / Infrastructure Foundation

| Increment | İçerik | Durum |
|---|---|---|
| 4.1 | Schema-per-module topology (init SQL: 8 şema; ADR-0001/0009) | ✅ tamamlandı |
| 4.2 | Docker Compose (yalnız postgres; volume + healthcheck + .env) | ✅ tamamlandı |
| 4.3 | Tek connection string + config override + API `/health` PostgreSQL check'i | ✅ tamamlandı |
| 4.4 | `Wms.PersistenceTests`: config resolution + gerçek bağlantı + schema varlığı | ✅ tamamlandı |
| 4.5 | Modül DbContext'leri + modül-own migration altyapısı | ⏳ Phase 5 ile (EF boş ceremony üretilmedi — ADR-0009) |

### Phase 5 — MasterData ✅

| Increment | İçerik |
|---|---|
| 5.1 | Product + SKU domain (barkod, boyut, ağırlık) |
| 5.2 | Barcode + UOM + kategori/brand |
| 5.3 | Persistence + CRUD API + SKU arama endpoint'i |

**Kural:** MasterData'da stok kolonu yok. Ayrıntı: [MASTER_DATA_MODEL.md](MASTER_DATA_MODEL.md).

### Phase 6 — Facility ✅

| Increment | İçerik |
|---|---|
| 6.1 | Warehouse aggregate |
| 6.2 | Hiyerarşik Location (self-reference parent) + LocationType |
| 6.3 | Capability'ler + validasyonlar (cycle, parent-warehouse, code unique, leaf-stock) |
| 6.4 | Persistence + API + location tree endpoint'i; deactivate/retire lifecycle (hard delete yok) |

**Acceptance:** Bursa (Zone→Aisle→Rack→Level→Bin) ve İstanbul (Floor/Pallet/Cold Storage)
farklı topolojiler olarak kurulabilir; blokajlı location'a putaway reddi testi (Inventory gelince).

### Phase 7 — Inventory Core ✅

| Increment | İçerik |
|---|---|
| 7.1 | InventoryBalance (durum bölmeli) + InventoryTransaction (ledger) şeması + constraint'ler |
| 7.2 | `Receive` + `Adjust` komutları (idempotency anahtarlı, FK'sız referans doğrulaması) |
| 7.3 | Status değişimleri (AVAILABLE/HOLD/QUARANTINE/DAMAGED) |
| 7.4 | Okuma: location contents, warehouse inventory, SKU×warehouse özeti |

**Acceptance:** negatif stok oluşturulamaz (unit + integration); her mutasyon ledger bırakır;
HOLD/QUARANTINE/DAMAGED'a allocation reddi unit testi.

### Phase 8 — Internal Movement ✅

| Increment | İçerik |
|---|---|
| 8.1 | `RelocateStock` (location→location, AVAILABLE, yalnız serbest stok) |
| 8.2 | `ChangeInventoryStatus` (statü yeniden sınıflandırma — allocated korunur) |
| 8.3 | Warehouse-total invariant testleri + concurrent over-move + deadlock güvenli kilit sıralaması |

### Phase 9 — Inbound ✅

| Increment | İçerik |
|---|---|
| 9.1 | InboundShipment + ASN (supplier) — MVP: `InboundReceipt` (externalReference/SourceType generic; PO/ASN module YOK) |
| 9.2 | Receiving (RECV/STAGING lokasyonlarına Receive) — `ReceiveItems` + Inventory `ReceiveInventory` kontratı + append-only receive records + MATCHED/SHORT/OVER + AVAILABLE/HOLD/QUARANTINE/DAMAGED partition |
| 9.3 | Putaway (putaway task → RCV'den storage'a scan-enforced relocation — Phase 8.5 reuse) |

**Acceptance:** Tedarikçi → Receiving → Putaway → Storage uçtan uca; putaway blokajlı
location'a yapılamaz; putaway yönlendirmesi capability'e göre reddedilir; retry asla stok
kopyalamaz; receipt tüm putaway task'ları bitmeden COMPLETED olmaz.

**Phase 9 özeti (bkz. INBOUND_MODEL.md):**

- `InboundReceipt` aggregate (OPEN → PARTIALLY_RECEIVED → RECEIVED → PUTAWAY_IN_PROGRESS →
  COMPLETED; yalnız OPEN → CANCELLED) + `InboundReceiptLine` + append-only
  `ReceiptLineReceiveRecord` (UNIQUE request_id) + `PutawayTask`
  (UNIQUE receive_record_id → aynı receive iki task üretemez).
- Inbound **asla** `inventory_balance`/`inventory_ledger` yazmaz — tek mutation yolu
  `IInventoryContract.ReceiveInventoryAsync` (atomic: balance + RECEIVED ledger +
  inventory_operation idempotency) ve putaway için `IInventoryContract.ExecuteScannedRelocationAsync`
  (Phase 8.5'in birebir aynı motoru).
- Cross-module crash/retry recovery: aynı RequestId ile retry → Inventory DuplicateRequest
  (ikinci +Quantity yok), Inbound eksik kaydını güvenle tamamlar (testli).
- Over-receipt policy: `Inbound:AllowOverReceipt` config (default false — strict).
- Receive yalnız RECEIVING/STAGING tipi, aktif, warehouse'a ait ve HoldsInventory=true
  lokasyonlara yapılır.
- Receive sonrası fiziksel cancel YOK (stok sisteme girmişse explicit correction gerekir).
- Putaway complete: source scan == task source (PUTAWAY_SOURCE_MISMATCH), barcode == task SKU
  (PUTAWAY_SKU_MISMATCH), destination Phase 8.5 validasyonu; movement başarılıysa task COMPLETED.
  Non-AVAILABLE putaway bilinçli olarak ertelendi (PUTAWAY_STATUS_NOT_SUPPORTED).
- DB: `inbound` şeması (4 tablo, cross-module FK YOK); migration InitialInbound uygulandı.
- 29 yeni test (hepsi gerçek PostgreSQL üzerinde, concurrency dahil) — tam süit 257/257.

### Phase 10 — Outbound ✅

| Increment | İçerik |
|---|---|
| 10.1 | FulfillmentOrder ingest (OMS contract, idempotent) — `CreateFulfillmentOrder` (RequestId + UNIQUE order_number) |
| 10.2 | Allocation (Inventory kontratı: `ReserveOrder` — tüm line'lar tek tx, all-or-nothing, deterministik per-line RequestId — ADR-0006) |
| 10.3 | Pick task → pick confirm (location+barcode scan; kısmi pick destekli, exceed yasak) + NotFound → Accuracy sinyali + PICK_EXCEPTION |
| 10.4 | Pack + shipment + ship → `ConsumeReservation` (idempotent; crash recovery testli) |

**Acceptance (kritik):** `Available=1` iken iki eşzamanlı allocation → tek başarı.
Pick confirm çift uygulanamaz (idempotency). Ship sonrası stok düşer.

**Phase 10 özeti (bkz. OUTBOUND_MODEL.md):**

- Outbound: FulfillmentOrder (+Lines), PickTask (location-level, UNIQUE(reservation_line_id)),
  Package (UNIQUE(order_id)), Shipment (UNIQUE(order_id)+UNIQUE(request_id)) — `outbound` şeması.
- Inventory tarafı: `ReserveOrder` (all-or-nothing, sku_id artan kilit sırası), contract'a
  `ReserveOrderAsync` + `GetReservationAsync` (+ ReservationLineId/SkuId bilgileri).
- NotFound gerçek workflow'dan Accuracy'ye bağlandı: signal + risk RED + CycleCountTask (testli).
- 25 yeni test (tümü gerçek PostgreSQL; concurrent allocate + crash recovery dahil) —
  tam süit 282/282.

### Phase 11 — Network View ✅

| Increment | İçerik |
|---|---|
| 11.1 | SKU network görünümü: Network → Warehouse → Location drill-down (canlı agregasyon — projection YOK) |
| 11.2 | Network summary + multi-SKU batch availability + read-only risk context |
| 11.3 | Transfer öncesi hazırlık: network OnHand okuma kontratı (`IInventoryContract` read yüzeyi) |

**Phase 11 özeti (bkz. NETWORK_INVENTORY_MODEL.md):**

- Read model / canlı aggregasyon; Fulfillment modülünde (yeni modül YOK — ADR-0005).
  Ayrı writable tablo YOK (testli); fulfillment şeması boş.
- Semantics: Warehouse Physical = AVAILABLE+HOLD+QUARANTINE+DAMAGED; Warehouse ATP =
  Σ AVAILABLE(quantity−allocated); Network = Σ warehouse (+ gelecekte InTransit yalnız Physical'a).
- Inventory contract read yüzeyi: SQL GROUP BY aggregasyonlar (sku×warehouse rollup,
  location+status breakdown, pagination, risk batch); MasterData `SearchSkuIds`/`GetSkusByIds`.
- API: `/api/network/inventory/skus[/{id}]` · `/warehouses/{id}` · `/summary` · `POST /availability`.
- 13 yeni test (gerçek PostgreSQL: aggregation, ATP ayrımı, inactive warehouse, risk
  read-only, mutation yansıması, pagination/filtre/sort, büyük dataset) — tam süit 295/295.

### Phase 12 — Transfers ✅

| Increment | İçerik |
|---|---|
| 12.1 | TransferOrder + TransferLine + state machine |
| 12.2 | Kaynak akış: Outbound kontratı üzerinden allocate/pick/pack/ship + `TransferOut` |
| 12.3 | InTransit muhasebesi (türetilmiş: shipped − received − confirmedVariance) + network görünümüne entegrasyon |
| 12.4 | Hedef akış: Inbound kontratı üzerinden receiving + `TransferIn` + putaway (Inbound otomatik task üretir) |
| 12.5 | Discrepancy (SHORT/DAMAGED_IN_TRANSIT/LOST/OVER/OTHER, append-only audit) |

**Acceptance (kritik):** Multi-warehouse senaryo (A içi move → toplam değişmez; A→B 100
transfer → shipped'te A düşer, InTransit=100, B henüz artmaz; received'da InTransit=0,
B +100; transfer-iz toplamı her adımda sabit — transfer-op nötrlüğü).

**Phase 12 özeti (bkz. TRANSFER_MODEL.md):** InTransit derived (writable kolon YOK, DB CHECK
negatif engeli); source=Outbound order path (deterministik RequestId), destination=Inbound
receipt path; yeni contract'lar (IOutboundContract/IInboundContract/ITransferContract);
crash/retry iki yönlü testli; over receipt explicit ret; network physical sabit invariant testli.
23 yeni test — tam süit 318/318.

### Phase 13 — Event Integration ✅

| Increment | İçerik |
|---|---|
| 13.1 | RabbitMQ (Docker) + modül-own outbox table + Integration relay (dispatcher) |
| 13.2 | Inbox + idempotent consumer + DLQ |
| 13.3 | İlk gerçek event akışı: ShipmentShipped + ReceiptCompleted → Transfers |

**Kural:** Bu faz yalnızca gerçek sınır varsa açılır; monolit içi iletişim broker'a taşınmaz.

**Phase 13 özeti (bkz. INTEGRATION.md):** `Wms.Integration` teknik assembly (envelope +
DTO'lar + Outbox/Inbox entity'leri + dispatcher + consumer host); Outbound/Inbound kendi
transaction'larında outbox yazar (`outbound.outbox_message`, `inbound.outbox_message`),
Transfers `transfers.inbox_message` (UNIQUE(consumer,event_id)) ile duplicate korur;
RabbitMQ `wms-integration` exchange + DLX/DLQ; retry backoff 5s→30s→5dk (event asla silinmez);
consumer manual ack + 1 redelivery sonrası DLQ; broker-down recovery gerçek RabbitMQ ile
testli; event path canlı doğrulandı (transfer ship API'si çağrılmadan IN_TRANSIT'e geçti).
14 yeni test — tam süit 332/332.

### Phase 14 — Fulfillment Sourcing ✅

| Increment | İçerik |
|---|---|
| 14.1 | `SourcingOptions` policy (config) + deterministic scoring |
| 14.2 | Aday değerlendirme: stok kapsamı, split penalty, reliability (risk) — mesafe Phase 15 |
| 14.3 | Açıklanabilir sonuç modeli + API + commit (reservation + Outbound order) |

**Acceptance:** Sipariş için "hangi depo + skor + nedenler" üretilir; split gerektiren
senaryo cezalandırılır; karar açıklanabilir.

**Phase 14 özeti (bkz. SOURCING_MODEL.md):** Evaluate (batch ATP, aktif-only adaylar, bounded
split kombinasyonları, config'lı skor + açıklamalar + shortage raporu — stok mutate ETMEZ) +
Commit (deterministik RequestId'li Outbound order + ReserveOrder; stale → SOURCING_STALE +
cancel; duplicate commit idempotent). `fulfillment` şeması (request/decision/link audit tabloları).
15 yeni test — tam süit 347/347; canlı e2e: evaluate → split plan → commit → 2 order.

### Phase 15 — Optimization ✅ (sourcing cost/route/split)

| Increment | İçerik |
|---|---|
| 15.1 | FulfillmentCostModel (9 kalem, config-driven, decimal para) + IRouteProvider (OSRM/Haversine/fallback/cache) |
| 15.2 | NearestAvailable / GreedyCoverage / Optimized (bounded exhaustive; OR-Tools-ready abstraction) + compare + counterfactual |
| 15.3 | Evaluate strateji/koordinat uzantısı + commit optimization snapshot (audit) |

**Phase 15 özeti (bkz. OPTIMIZATION_MODEL.md):** Optimizer yalnız Phase 14 feasible set'ini
maliyetle sıralar; stok mutate etmez; timeout → GREEDY_FALLBACK (açık işaretli); OSRM down →
HAVERSINE_FALLBACK (açık işaretli); eski repo için deterministik regression fixture (lokal repo
yoktu — elle doğrulanmış baseline). 19 yeni test — tam süit 366/366; canlı compare e2e
(OSRM down → fallback + timeout yolları dahil) doğrulandı.

### Phase 16 — Observability ✅

| Increment | İçerik |
|---|---|
| 16.1 | System Integrity Gate: E2E senaryolar (normal order, phantom inventory, transfer invariant, concurrent last-stock, duplicate hammer, broker down/recovery) |
| 16.2 | OpenTelemetry + Prometheus (/metrics) + Grafana (provisioned dashboard) + OSRM Degraded health + outbox retention |

**Phase 16 özeti (bkz. OBSERVABILITY.md):** 20+ ölçekli metrik seti (inventory/outbound/
inbound/messaging/sourcing), structured log kuralları, retention job (yalnız published),
broker stop/start gerçek docker testi, cross-schema FK global kontrolü, duplicate writable
stock scan (Domain-katmanı), health endpoint'leri. 8 yeni integrity testi — tam süit
374/374; Prometheus scrape + Grafana dashboard canlı doğrulandı.

### Phase 17 — Web UI & Demo Dağıtımı ✅

| Increment | İçerik |
|---|---|
| 17.1 | React 18 + TypeScript + Vite SPA (`apps/wms-simulator-web`): 11 sayfa (Overview, Network, Warehouses, Inventory, Inbound, Outbound, Transfers, Accuracy, Sourcing, Scenarios, Operations) — hepsi gerçek API endpoint'lerini kullanır; mock yok |
| 17.2 | Demo scenario endpoint düzeltmesi (`[FromServices]` + DI registration) ve `ScenarioInitializer` idempotent kurulum |
| 17.3 | Başlangıç migration runner'ı (`DbMigrator`): 7 modül şeması API startup'ında sırayla uygulanır — fresh DB'de manuel adım yok |
| 17.4 | Docker dağıtımı: `src/Wms.Api/Dockerfile` + web Dockerfile (nginx, `/api` proxy) + compose'a `api`/`web` servisleri; imajlar build edildi ve canlı doğrulandı |
| 17.5 | E2E kabul testleri (Playwright, gerçek backend + PostgreSQL + RabbitMQ): 6 sıralı senaryo — Receive→Putaway→Inventory, Order→Ship, NotFound→CycleCount→Reconciliation, Transfer partial receive, Sourcing compare, SOURCING_STALE |

**Phase 17 özeti (bkz. DEMO_GUIDE.md):** Web UI tüm operasyon akışlarını uçtan uca
yürütür; demo verisi Scenarios sayfasından `POST /api/dev/scenarios/{scenario}/initialize`
ile kurulur. E2E süitinde tespit edilen iki gerçek hata düzeltildi: (1) `POST /api/skus`
ürün ilişkisi zorunlu (E2E artık product oluşturuyor), (2) cycle-count risk değerlendirmesi
N+1 — `ListRiskAssessments` warehouse-bazlı batch sorgulara (`GetWarehouse*Async`) taşındı,
büyük veride evaluate saniyeler içinde biter. Docker full stack (web→nginx→api→postgres/rabbitmq)
canlı doğrulandı; `docker compose up -d --build` ile tek komutta demo hazır.

## Cross-Cutting Plan

- **Testler:** Unit (her fazda invariant'lar) + Integration (PostgreSQL ile: concurrency,
  constraint, outbox, FK'sız referans doğrulaması) + E2E senaryolar (her fazın acceptance'ı).
  Test altyapısı Phase 3'te kuruldu (arch) + Phase 4'te persistence gate eklendi.
- **UI:** Phase 6'dan itibaren domain fazlarına paralel basit ekranlar; navigasyon
  (Dashboard, Depolar, Stok, Giriş, Siparişler, Transferler, Raporlar, Ayarlar) + global
  warehouse selector. UI teknoloji seçimi Phase 3'te netleşir (basitlik öncelikli).
- **Dokümantasyon:** Her faz sonunda bu belge seti + ADR'ler güncellenir.

## Gelecek Alan: Inventory Accuracy / Stock Integrity (Roadmap'e eklendi)

```text
Inventory Accuracy / Stock Integrity
├── 8.1 Signal Foundation (PICK_NOT_FOUND + snapshot + idempotency)      ✅ Phase 8.1
├── 8.2 Velocity / ABC-Dead Analysis · PickNotFound risk kuralları        ✅ Phase 8.2
├── 8.3 Dynamic Cycle Counting (blind count + variance + stale policy)    ✅ Phase 8.3
├── 8.4 Reconciliation & Controlled Adjustment                            ✅ Phase 8.4
└── 8.5 Scan-Enforced Movement / Smart Putaway                            ✅ Phase 8.5
```

8.1-8.5 tamamlandı — bkz. [INVENTORY_MODEL.md](INVENTORY_MODEL.md).
**Prensip: Stock is never silently overwritten; every correction must be explainable
from physical evidence to ledger entry.**

8.5 kapsamında uygulananlar (bkz. INVENTORY_MODEL.md §8.5):

- `ExecuteScannedRelocation` — RF tipi scan tabanlı putaway/relocation:
  - Girdi: (source location scan, SKU barcode scan, destination location scan) + quantity.
  - Server tarafı scan çözümleme: location code → `IFacilityQueryContract.GetLocationByCodeAsync/Global`,
    barcode → `IMasterDataQueryContract.GetSkuByBarcodeAsync`.
  - Strict mode: üç scan de zorunlu; eksik scan asla yumuşak geçirilmez.
  - Açık rejection kodları: `SCAN_REQUIRED`, `SOURCE_NOT_FOUND`, `SKU_NOT_FOUND`,
    `DESTINATION_NOT_FOUND`, `LOCATION_INACTIVE`, `WRONG_WAREHOUSE`,
    `SKU_NOT_AT_SOURCE`, `INSUFFICIENT_AVAILABLE_STOCK`, `DESTINATION_NOT_ALLOWED`.
  - Destination politikası: `HoldsInventory=false` veya Dock/Shipping/Staging/Packing/
    Receiving/CrossDock tipi lokasyonlar putaway kabul etmez (yorumlanabilir ret).
  - Hareketin kendisi mevcut `RelocateStock` engine'ini kullanır (çift engine YOK) —
    dolayısıyla row-lock, allocated koruması, duplicate RequestId idempotency ve
    ledger yazımı 8.1-8.4 ile birebir aynıdır.
- `ScanMovementEvidence` (append-only): her scan'lı hareket için movement ile AYNI
  transaction'da yazılan kanıt kaydı (scan değerleri, device id, operator id).
  `inventory.scan_movement_evidence`, UNIQUE(movement_id).
- RF endpoint: `POST /api/inventory/movements/scanned-relocation` —
  ret → `400` + `{status, rejectionCode, rejectionReason}`; başarı → `{status: Completed, movementId, evidenceId}`.
- Kabul: 21 test (geçerli 3-scan akışı, 3 eksik-scan strict ret, çözümleme hataları,
  inactive/foreign warehouse, yasak destination tipi, SKU@source yok, yetersiz/allocated,
  duplicate + concurrent duplicate idempotency, warehouse-total invariant, evidence atomicity,
  gerçek PostgreSQL round-trip) + canlı API doğrulaması.

## Bilinçli Ertelemeler

| Konu | Ne zaman? |
|---|---|
| Redis | Gerçek ihtiyaç (cache) ölçülünce |
| NetworkInventoryProjection (event-beslemeli) | Phase 11/13 ihtiyacıyla |
| Carrier/OMS event entegrasyonu | Phase 13 |
| Transfer discrepancy resolution akışı (short/lost/damaged/over) | Phase 12 sonrası — model engellemez (TRANSFER_MODEL.md) |
| Wave planning, batch picking | Phase 15 |
| Warehouse Runtime ayrıştırması | Ölçek/coğrafi ihtiyaç (deployment evrimi) |
