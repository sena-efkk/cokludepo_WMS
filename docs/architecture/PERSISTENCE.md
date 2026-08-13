# Persistence Strategy (Phase 4 temeli)

> PostgreSQL topology, şema sahipliği, connection ve migration stratejisi.
> Kararların kaynağı: ADR-0001, ADR-0009. Bu belge uygulamaya yakın özetidir.

## PostgreSQL Topology (MVP)

```text
WMS (tek ASP.NET Core app)
 │
 ▼
Tek PostgreSQL instance — tek database (wms) — TEK connection string
 │
 ├── master_data      → MasterData modülü
 ├── facility         → Facility modülü
 ├── inventory        → Inventory modülü
 ├── inbound          → Inbound modülü
 ├── outbound         → Outbound modülü
 ├── transfers        → Transfers modülü
 ├── fulfillment      → Fulfillment modülü
 └── administration   → Administration modülü
```

- Sekiz ayrı database YOK; schema-per-module ile tek DB'de izolasyon (ADR-0001).
- Şemalar container ilk init'inde `deploy/postgres/init/01-create-schemas.sql` ile oluşturulur
  (yalnız boş volume'de çalışır; tekrar çalışmaz — init script özelliği).
- `public` şemasına uygulama verisi YAZILMAZ (genel çöplük yasak).

## Modül Sahipliği Kuralları

| Kural | Durum |
|---|---|
| Modül kendi şemasının sahibidir | Kalıcı |
| Modül İÇİ FK kullanılabilir | Kalıcı |
| Cross-module FK kullanılmaz | Kalıcı (ADR-0001) |
| Başka modülün tablosuna doğrudan mutation YOK | Kalıcı (kontratlar üzerinden) |
| Hard delete yasak; deactivate/retire | Kalıcı (MULTI_WAREHOUSE_ISOLATION.md) |

## Migration Ownership (kural şimdiden sabit — ADR-0009)

- Her modül **kendi migration geçmişinin sahibidir**:

```text
Wms.Modules.MasterData/Infrastructure/Persistence/Migrations
Wms.Modules.Facility/Infrastructure/Persistence/Migrations
Wms.Modules.Inventory/Infrastructure/Persistence/Migrations
...
```

- Tüm migration'lar `Wms.Api/Migrations` altına YIĞILMAZ.
- Phase 4'te entity yok → migration YOK. İlk gerçek persistence modeli Phase 5
  (MasterData) ile geldiğinde ilgili modül kendi `DbContext`'ini ve migration'larını oluşturur.
- Hedef isimlendirme: `MasterDataDbContext` → `master_data`, `FacilityDbContext` → `facility`, ...

## Connection Strategy

- **Tek connection string**: `ConnectionStrings:WmsDatabase`.
  - `appsettings.json`'da boş default; process env var `ConnectionStrings__WmsDatabase` override eder (standart .NET config).
  - Sekiz duplicate connection string YOK.
- Local geliştirme akışı:
  1. `deploy/` içinde `.env.example` → `.env` kopyala, değerleri doldur (gerçek `.env` gitignore'da).
  2. `docker compose up -d` (deploy/ içinde) → PostgreSQL hazır, 8 şema oluşur.
  3. API: `ConnectionStrings__WmsDatabase="Host=localhost;Port=5432;Database=<POSTGRES_DB>;Username=<POSTGRES_USER>;Password=<POSTGRES_PASSWORD>"`.
- Secret'lar repo'da YOK; yalnızca local `.env` (gitignore) ve process env var.

## Persistence Abstraction Disiplini (YAGNI + yasaklar)

- Phase 4'te EF Core/Npgsql package'ları **8 modüle dağıtılmadı**; boş DbContext/fake entity/
  boş migration üretilmedi. `Wms.Api` yalnız health-check için Npgsql kullanır (infra smoke).
- Yasak (kalıcı): `IRepository<T>`, `GenericRepository<T>`, `GlobalUnitOfWork`, `WmsDbContext`
  (dev shared context). Persistence abstraction gerçek use-case ile tasarlanacak —
  EF Core'un atomik update/transaction/concurrency/constraint özellikleri gizlenmeyecek.

## Inventory Geleceğini Engellememe Kontrol Listesi (Phase 7+ hazırlık)

Foundation şu gereksinimlerin önünü KAPATMIYOR; hiçbiri şimdi implement edilmedi:

| Gereksinim | Nasıl mümkün kalacak |
|---|---|
| Atomik allocation (`UPDATE ... WHERE quantity - allocated >= @q`) | Raw SQL/`ExecuteUpdate` — repository abstraction'ı yok, doğrudan DbContext kullanılabilir |
| Optimistic concurrency | EF `xmin`/rowversion mapping veya raw SQL — engel yok |
| DB constraint'ler (quantity >= 0 vb.) | Migration'larda CHECK/UNIQUE serbest; modül şeması bizim |
| Balance + Ledger aynı tx | DbContext'ler ortak connection/tx kapsamı kurulabilir; global UoW olmadan use-case seviyesinde |
| Append-only ledger / audit / cycle count / adjustment reason / idempotency key / movement history | Migration sahipliği modülde; şema tasarımı özgür — engel yok |
| Outbox (ileride) | Modül-own outbox tablosu + iş state'iyle aynı tx — generic repo engeli YOK (ADR-0001) |

## Inventory Accuracy Gelecek Alanı (Roadmap'e ekli — Phase 8.1-8.5'te implement edildi)

```text
Inventory Accuracy / Stock Integrity
├── Velocity / ABC-Dead Analysis          ✅ Phase 8.2
├── PickNotFound signals                  ✅ Phase 8.1
├── Risk-Based Cycle Counting             ✅ Phase 8.3
├── Reconciliation                        ✅ Phase 8.4
└── Scan-Enforced Movement / Smart Putaway ✅ Phase 8.5
```

Phase 4'te bunlar yalnızca tasarım olarak öngörülmüştü; 8.1-8.5'te tamamı inventory
şemasında kendi migration'larıyla uygulandı (accuracy signals, cycle count, reconciliation,
scan_movement_evidence). Tasarımın ek tablolara izin verdiği doğrulandı.

## Doğrulama

- `docker compose config` ✅ · `docker compose up -d` ✅ (healthcheck healthy)
- `Wms.PersistenceTests`: config resolution + gerçek bağlantı + 8 şema varlığı ✅
- API `/health`: `{"status":"Healthy", "self": Healthy, "postgresql": Healthy}` ✅
