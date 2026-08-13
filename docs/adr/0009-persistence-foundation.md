# ADR-0009: Persistence Foundation — Tek Connection, Schema Ownership, Migration Sahipliği, EF Ertelendi

- **Tarih:** 2026-08-13 (Phase 4)
- **Durum:** Accepted

## Context

Phase 4'te PostgreSQL altyapısı kurulacak ancak henüz business entity yok. Sekiz modüle
EF Core dağıtmak, boş DbContext/fake entity/boş migration üretmek YAGNI ihlali olurdu.
Aynı zamanda ilerideki Inventory gereksinimleri (atomik allocation, optimistic concurrency,
DB constraint'ler, balance+ledger aynı tx, outbox) bugünkü kararlarla engellenmemeli.

## Decision

1. **Tek PostgreSQL instance + tek database + tek connection string**
   (`ConnectionStrings:WmsDatabase`). Sekiz ayrı DB/connection YOK.
2. **Schema-per-module** (ADR-0001 ile uyumlu): `master_data`, `facility`, `inventory`,
   `inbound`, `outbound`, `transfers`, `fulfillment`, `administration`. Şemalar container
   init script'iyle oluşturulur (`deploy/postgres/init/01-create-schemas.sql`); `public`
   şemasına uygulama verisi yazılmaz.
3. **Migration sahipliği modülündür**: her modül kendi `Infrastructure/Persistence/Migrations`
   geçmişinin sahibi olacak; merkezi `Wms.Api/Migrations` yığını YOK. Phase 4'te migration YOK —
   ilk model Phase 5 MasterData ile gelir, modül kendi DbContext'ini kurar.
4. **EF Core şimdi paketlenmedi**: business module'lere EF/Npgsql dağıtılmadı; boş ceremony
   üretilmedi. `Wms.Api` yalnızca health-check smoke'u için Npgsql (10.0.3) kullanır.
5. **Connection yapılandırması**: `appsettings.json`'da boş default + `ConnectionStrings__WmsDatabase`
   env var override (standart .NET config). Secret'lar: `deploy/.env` (gitignore) + process env var;
   `.env.example` yalnızca boş şablon.
6. **Persistence abstraction yasakları kalıcı**: `IRepository<T>`, `GenericRepository<T>`,
   `GlobalUnitOfWork`, `WmsDbContext` üretilmez — EF yetenekleri (atomic update, tx, constraint,
   concurrency) soyutlama arkasına gizlenmez.
7. **Health check**: built-in `AddHealthChecks` + Npgsql `SELECT 1` (`PostgresHealthCheck`),
   özel JSON writer — ek framework paketi YOK.
8. **Test**: `Wms.PersistenceTests` (yeni proje): config resolution testleri + gerçek
   PostgreSQL bağlantı ve schema varlığı testleri. Connection: env var → `deploy/.env` fallback.
   Testcontainers KULLANILMADI — tek bağlantı testi için fazla altyapı; local compose yeterli.

## Alternatives

| Alternatif | Neden reddedildi |
|---|---|
| 8 ayrı database/connection string | MVP için operasyonel yük; şema izolasyonu yeterli (ADR-0001) |
| EF Core'u 8 modüle şimdi dağıtmak | Boş DbContext/fake entity/boş migration = ceremony; YAGNI |
| Merkezi migration klasörü (Wms.Api/Migrations) | Modül sahipliğini bozar; ayrışmayı zorlaştırır |
| Testcontainers | Tek bağlantı testi için gereksiz altyapı; local compose + skip/fail guard yeterli |
| Sekiz ayrı connection config anahtarı | Duplicate config; tek DB tek connection yeterli |

## Consequences

- ✅ En sade kurulum: `docker compose up -d` + tek env var → sistem bağlanır.
- ✅ Modül sahipliği DB'de görünür; migration geçmişleri modülde kalır.
- ⚠️ Şema init script'i yalnız ilk volume'de çalışır — yeni şema eklenirse var olan kurulumda
  manuel `CREATE SCHEMA` gerekir (dokümante; ileride migration'a taşınabilir).
- ⚠️ `Wms.PersistenceTests` bağlantı testleri PostgreSQL yoksa **bilinçli olarak başarısız olur**
  (env var + `.env` yoksa açıklayıcı mesajla) — bu test projesi DB gate'idir.
- ✅ Inventory fazları için hiçbir engel yok: raw SQL/`ExecuteUpdate`, xmin concurrency,
  constraint'ler, modül-own outbox tablosu — hepsi açık (PERSISTENCE.md kontrol listesi).
