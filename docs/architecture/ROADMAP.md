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
| 9 | **Inbound** | InboundShipment → Receiving → QC → Putaway | Tedarikçi girişi uçtan uca çalışır |
| 10 | **Outbound** | FulfillmentOrder → Allocation → Pick → Pack → Ship | Concurrency acceptance testi geçer (Available=1, iki sipariş) |
| 11 | **Network View** | SKU drill-down: Network → Warehouse → Location | SKU araması üç seviyeyi gösterir |
| 12 | **Transfers** | TransferOrder state machine + InTransit muhasebesi | Multi-warehouse acceptance senaryosu geçer |
| 13 | **Event Integration** | RabbitMQ + Outbox/Inbox (gerçek ihtiyaçla) | Duplicate message zararsız; outbox atomik |
| 14 | **Fulfillment Sourcing** | Aday depo skorlama (deterministic) + açıklanabilir sonuç | Karar: depo + skor + neden listesi |
| 15 | **Optimization** | Putaway strategy → slotting → picking route (tek tek) | Strateji abstraction'ı değiştirilebilir |
| 16 | **Observability** | OpenTelemetry + Prometheus + Grafana + structured logs | Operasyonlar ve anomaliler izlenebilir |

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

### Phase 9 — Inbound

| Increment | İçerik |
|---|---|
| 9.1 | InboundShipment + ASN (supplier) |
| 9.2 | Receiving (RCV/QC lokasyonlarına Receive) |
| 9.3 | Putaway (putaway task → RCV'den storage'a Move) |

**Acceptance:** Tedarikçi → Receiving → Putaway → Storage uçtan uca; putaway blokajlı
location'a yapılamaz; putaway yönlendirmesi capability'e göre reddedilir.

### Phase 10 — Outbound

| Increment | İçerik |
|---|---|
| 10.1 | FulfillmentOrder ingest (OMS contract, idempotent) |
| 10.2 | Allocation (Inventory kontratı: location seviyesi reservation + atomik koşullu update — ADR-0006) |
| 10.3 | Pick task → pick confirm (`Consume`) |
| 10.4 | Pack + staging + shipment + ship |

**Acceptance (kritik):** `Available=1` iken iki eşzamanlı allocation → tek başarı.
Pick confirm çift uygulanamaz (idempotency). Ship sonrası stok düşer.

### Phase 11 — Network View

| Increment | İçerik |
|---|---|
| 11.1 | SKU network görünümü: Network → Warehouse → Location drill-down (canlı agregasyon) |
| 11.2 | Basit UI ekranları: SKU arama, warehouse inventory, location contents |
| 11.3 | Transfer öncesi hazırlık: network OnHand okuma kontratı |

### Phase 12 — Transfers

| Increment | İçerik |
|---|---|
| 12.1 | TransferOrder + TransferLine + state machine |
| 12.2 | Kaynak akış: Outbound kontratı üzerinden allocate/pick/pack/ship + `TransferOut` |
| 12.3 | InTransit muhasebesi (türetilmiş: shipped − received) + network görünümüne entegrasyon |
| 12.4 | Hedef akış: Inbound kontratı üzerinden receiving + `TransferIn` + putaway |
| 12.5 | Transfer timeline UI + problemli transfer görünümü (açık InTransit pozisyonları) |

**Acceptance (kritik):** Multi-warehouse senaryo (A içi move → toplam değişmez; A→B 100
transfer → shipped'te A düşer, InTransit=100, B henüz artmaz; received'da InTransit=0,
B +100; transfer-iz toplamı her adımda sabit — transfer-op nötrlüğü).

### Phase 13 — Event Integration

| Increment | İçerik |
|---|---|
| 13.1 | RabbitMQ (Docker) + modül-own outbox table + Integration relay |
| 13.2 | Inbox + idempotent consumer + DLQ |
| 13.3 | İlk gerçek event akışı (ör. OMS async FulfillmentOrder veya Inventory→Network projection) |

**Kural:** Bu faz yalnızca gerçek sınır varsa açılır; monolit içi iletişim broker'a taşınmaz.

### Phase 14 — Fulfillment Sourcing

| Increment | İçerik |
|---|---|
| 14.1 | `IWarehouseSourcingStrategy` (deterministic scoring) |
| 14.2 | Aday değerlendirme: stok kapsamı, mesafe, split penalty, maliyet, SLA, cutoff |
| 14.3 | Açıklanabilir sonuç modeli + API + OMS bildirimi |

**Acceptance:** Sipariş için "hangi depo + skor + nedenler" üretilir; split gerektiren
senaryo cezalandırılır; karar UI'da açıklanabilir.

### Phase 15 — Optimization

Sırayla, tek tek (hepsi birden değil): PutawayStrategy → Slotting → PickingRoute →
Batch Picking → Wave Planning. Her biri strategy abstraction arkasında; core domain'e gömülmez.

### Phase 16 — Observability

OpenTelemetry (tracing + metrics), Prometheus, Grafana OSS, yapılandırılmış loglar +
CorrelationId; balance↔ledger uzlaşma kontrolü, orphan referanslar ve transfer anomalileri
dashboard'a taşınır.

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
