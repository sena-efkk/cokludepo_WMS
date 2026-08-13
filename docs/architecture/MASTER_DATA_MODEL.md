# MasterData Model (Phase 5)

> "Bu ürün nedir?" — MasterData'nın tek sorusu. "Nerede? Kaç tane? Rezerve mi?"
> Inventory/Facility/Outbound domain'lerinindir. MasterData'da stok alanı ASLA yoktur.

## Model

```text
Brand ─┐
       ├── Product ── SKU ── SkuBarcode (0..n)
Category┘               │
                        └── UOM (zorunlu)
```

| Varlık | Açıklama | Önemli alanlar |
|---|---|---|
| **Product** | Genel ürün kimliği ("Basic T-Shirt") | Name (zorunlu), Description?, BrandId?, CategoryId?, IsActive |
| **SKU** | Stoklanabilir/satılabilir varyant ("Basic T-Shirt — Siyah S") | ProductId, **Code** (internal, UNIQUE), Name?, UomId, ağırlık/boyutlar, IsActive |
| **SkuBarcode** | Dış tanımlayıcılar (EAN/UPC/supplier) — **SKU başına birden fazla** | Value (UNIQUE), Type |
| **UOM** | Ölçü birimi (EA, BOX, PCS, KG — seed'li) | Code (UNIQUE), Name |
| **Brand / Category** | Basit referans listeleri (hierarchy YOK — MVP) | Name (UNIQUE) |

**Kritik ayrımlar:**

- `SKU.Code` (SKU-000001) ≠ barcode (8691234567890): internal kimlik vs dış tanımlayıcı.
- `SkuBarcode` ayrı tablo: tek barcode kilidi yok, polymorphic identifier framework YOK.
- **Quantity/WarehouseId/stock yok** — `Product.Quantity` gibi alanlar YASAKTIR.

## SKU Code Generation

- Internal format: `SKU-{seq:D6}` → `SKU-000001`...
- Kaynak: PostgreSQL sequence `master_data.sku_code_seq` (migration ile, model'e `HasSequence` olarak kayıtlı) — concurrent-safe, deterministic, test edilebilir.
- DB tarafında `sku.code` UNIQUE index son savunma.
- Generator: `Application.SkuCodeGenerator` (entity'ye gömülü DEĞİL).

## Lifecycle (hard delete YOK)

- `IsActive=false` (deactivate) — Inventory ileride SkuId referanslayacak; cross-module FK olmadığından (ADR-0001) fiziksel silme tarihsel tutarlılığı bozar.
- Deactivate idempotent; default listelerde inaktif SKU görünmez; kodu yeniden kullanılamaz (UNIQUE korur).

## Import / Canonical Model (Anti-Corruption Layer)

```text
External Source (CSV/Excel/REST/ERP/izinli scraping — GELECEKTE)
        ↓
Adapter / Importer (module dışı, henüz yok)
        ↓
Canonical: ProductCatalogItemInput  (Application)
        ↓
ImportCatalog use-case  →  Product / SKU / SkuBarcode / Brand / Category
```

- **Core domain dış kaynak DTO'sunu, HTML/CSS selector'ı, SAP movement type'ı BİLMEZ** —
  adaptör canonical input üretir, use-case onu tüketir.
- Idempotency: SKU kodu veya barcode mevcutsa satır atlanır (skip) — tekrar import veri patlatmaz.
- `ExternalId` input'ta taşınır (ileride kaynak eşleme için), MVP'de persist edilmez.

## Synthetic Catalog (demo)

- `Application.Import.SyntheticCatalogFactory` — 36 SKU / 21 Product, 5 kategori
  (Tekstil, Kırtasiye, Ev Yaşam, Kozmetik, Elektronik Aksesuar), sentetik markalar,
  deterministik EAN-13 benzeri barcode'lar (`869...`). Rastgelelik YOK → tekrarlanabilir test.
- Üretim domain davranışını DEĞİŞTİRMEZ: aynı `ImportCatalog` pipeline'ından geçer.
- Çalıştırma: `POST /api/catalog/seed-demo` (tekrar çalıştırmada 0 created / N skipped).

## API (Phase 5)

```text
POST /api/products                CreateProduct
GET  /api/products?search=        ListProducts (ILike, default aktif)
GET  /api/products/{id}
POST /api/skus                    CreateSku (code yoksa üretilir; duplicate → 409)
GET  /api/skus?search=&productId=&includeInactive=
GET  /api/skus/{id}
POST /api/skus/{id}/deactivate    204 (idempotent)
POST /api/catalog/import          canonical input listesi
POST /api/catalog/seed-demo       sentetik katalog (demo)
```

- HTTP DTO'lar domain entity değildir (`Wms.Api.MasterData.*Dtos`).
- Hatalar domain dilinde: 404 (NotFound), 409 (DuplicateSku), 400 (validation) — asla 500'e düşmez.
- SKU arama: code / name / barcode / **product adı** ILIKE (PostgreSQL; Elasticsearch YOK).

## Persistence

- `MasterDataDbContext` → `master_data` şeması (ilk gerçek modül DbContext'i).
- Migration'lar modül içinde: `Infrastructure/Persistence/Migrations/`
  (`InitialMasterData` + `AddSkuCodeSequence`); geçmiş tablosu `master_data.__ef_migrations_history`.
- Fluent API + snake_case naming convention; domain'de EF attribute'u YOK.
- DB constraint'ler: `sku.code` UNIQUE, `sku_barcode.value` UNIQUE, `sku.weight/... >= 0` CHECK,
  `uom.code`/`brand.name`/`category.name` UNIQUE, UOM seed data.
- Generic repository YOK — `IMasterDataStore` use-case odaklı port, EF implementasyonu Infrastructure'da.

## Testler

- Domain: boş kod/ürün/uom reddi, negatif ölçü reddi, deactivate, barcode duplicate guard.
- Application (fake store): kod üretimi (SKU-000007), duplicate code/barcode → hata, bilinmeyen
  product/uom, default UOM=EA, deactivate idempotent.
- Persistence (gerçek PostgreSQL): migration + şema tabloları, UNIQUE constraint'ler
  (DbUpdateException), roundtrip, sequence artışı, sentetik import idempotency + geçerlilik.
- Synthetic: deterministik, 30-100 aralığı, unique barcode, 5 kategori.
- Architecture testleri aynen geçiyor (modül DAG, katman kuralları — MasterData'nın gerçek
  Domain koduyla artık gerçek tipler üzerinde çalışıyor).
