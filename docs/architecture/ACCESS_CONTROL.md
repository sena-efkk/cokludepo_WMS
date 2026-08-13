# User / Warehouse Access Model

> Kim, hangi depoda, ne yapabilir? Yetki backend'de enforce edilir; frontend yalnızca gizler.
> Karar kaydı: **ADR-0007**.

## Authentication vs Authorization (kesin ayrım)

| Soru | Sahibi | Teknoloji |
|---|---|---|
| **Authentication** — "Bu kullanıcı **kim**?" | Teknik altyapı | ASP.NET Core Identity (self-hosted, ücretsiz) + JWT |
| **Authorization / Access Control** — "Bu kullanıcı **hangi warehouse** üzerinde **hangi işlemi** yapabilir?" | **Administration modülü** (domain/application ownership) | UserWarehouseAccess + policy'ler |

- ASP.NET Identity yalnızca kimlik doğrulama **altyapısıdır**; warehouse-scope erişim kuralı
  bir **domain kararıdır** ve Administration modülünün sahipliğindedir.
- Bu ayrımın karışması, erişim kurallarının controller'lara/Identity'ye gömülmesine ve
  "kim hangi depoya erişebilir" sorusunun sahipsiz kalmasına yol açar.

## Roller ve Kapsamlar

| Rol | Kapsam | Yetki Özeti |
|---|---|---|
| SystemAdmin | Company | Her şey: kullanıcı, depo kurulumu, config, master data |
| NetworkManager | Network | Tüm depoları görür; transfer, sourcing, network raporları yönetir |
| WarehouseManager | Warehouse (seçili) | Kendi depo(lar)ında tam operasyon: inbound, outbound, envanter düzeltme, location yönetimi |
| Operator | Warehouse (seçili) | Giriş/çıkış/putaway işlemleri; düzeltme yetkisi yok |
| Picker | Warehouse (seçili) | Yalnızca kendisine atanan pick task'leri |
| Viewer | Warehouse veya Network | Salt okunur |

## UserWarehouseAccess — Sorumluluk ve Model

```text
administration.app_user        → kimlik (Identity'nin User tablosu; domain görünümü modülde)
administration.app_role
administration.user_warehouse_access
  user_id, warehouse_id, role → (kullanıcı, depo) çiftine rol ataması
  UNIQUE (user_id, warehouse_id)
```

**Sorumlulukları:**

1. **Warehouse-scope erişim kümesinin writable truth'u**: "A kullanıcısı B deposunda hangi
   rolle çalışır?" sorusunun tek cevabı.
2. **Erişim kümesinin claim'lere çevrilmesi**: login'de `(role, warehouseIds)` çiftleri
   token/claim olarak üretilir — request body'den değil, buradan gelir.
3. **Atama lifecycle'ı**: depo açılışında manager ataması, kullanıcı ayrılışında revoke,
   rol değişimi — audit log'lanır.
4. **Network-scope roller** (NetworkManager, SystemAdmin) warehouse satırı taşımaz;
   erişim kümesi = tüm depolar (kural rol tanımından gelir).

## Backend Enforcement Mekanizması

```text
HTTP isteği
  → Authentication (JWT doğrulama)                       [teknik: Identity]
  → claims: userId, roller + erişilebilir warehouseId'ler [Administration üretir]
  → Authorization policy'leri (endpoint bazında)          [API katmanı, IAccessControl]
  → Mutation guard: WarehouseId parametresi ∈ erişim kümesi kontrolü (aksi → 403 WarehouseAccessDenied)
  → Query guard: warehouse-scope sorgular erişim kümesine otomatik daraltılır
     (filtre claim'lerden gelir — request body'den DEĞİL)
```

**Enforcement noktası önemli:** Bu mekanizma API/presentation katmanında **cross-cutting**
uygulanır; domain modülleri (Inventory, Outbound...) Administration'a bağımlı OLMAZ
(bkz. [MODULE_MAP.md](MODULE_MAP.md) — DAG notu).

Kurallar:

- Warehouse-scope endpoint'lerde `WarehouseId` **route/query'den** gelir ve erişim kümesiyle
  çapraz kontrol edilir. Kullanıcının body'ye istediği warehouseId'yi yazması yetki vermez.
- Global warehouse selector (`Depo: [Bursa ▼]`, `[Tüm Depolar]` yalnız network rolleri için)
  → seçili scope backend'e her istekte taşınır ve doğrulanır.
- `WarehouseAccessDenied`, `InvalidTransferTransition` gibi domain hataları standart error
  modeliyle döner (asla 500'e düşmez).

## Kabul Testleri (ROADMAP ile eşleşik)

1. Bursa WarehouseManager: Bursa envanterinde mutation → **başarılı**; İstanbul envanterinde → **403**.
2. NetworkViewer: birden fazla depo görür; herhangi bir stok mutation'ı → **403**.
3. Operator: inventory adjustment yapamaz (yetki dışı); receive/putaway yapabilir.
4. Warehouse-scope listeleme endpoint'leri, erişimi olmayan deponun verisini **hiçbir koşulda** döndürmez.

## Audit (stok ledger'ından ayrı)

`administration.audit_log` — "kim, neyi, ne zaman, hangi action'la değiştirdi":

- Kritik operasyonlar: Inventory Adjustment, Location Blocking, Transfer Cancel,
  Manual Stock Change, Receipt Correction, **WarehouseAccess değişiklikleri**.
- `inventory_transaction` stok hareketini anlatır; `audit_log` kullanıcı/system aktivitesini anlatır.
  İkisi farklı tablolar, farklı amaçlar (bkz. [DATA_OWNERSHIP.md](DATA_OWNERSHIP.md)).
- Actor bilgisi stok komutlarına `actor_id` olarak girer → ledger satırına işlenir.

## Not (Phase 3'te netleşecek)

- Kimlik sağlayıcı: ASP.NET Core Identity (self-hosted) default — dış ücretli auth servisi yok.
- JWT vs cookie: API-first tasarım nedeniyle JWT önerilir; Phase 3'te kararlaştırılır.
