# Consistency & Transaction Boundaries

> Hangi işlem hangi transaction içinde koşar, neden; hangi sınır güçlü tutarlılık,
> hangi sınır eventual consistency gerektirir. Bu projede öğrenmenin ana ekseni.

## İlkeler

1. **Fiziksel stok mutasyonları = güçlü tutarlılık.** Aynı DB transaction'ı + atomik koşullu
   UPDATE'ler + DB constraint'ler. Stok negatif olamaz; available asla yarışa kurban gitmez.
2. **Network akışları = eventual consistency.** Transfer, projection, dış entegrasyon:
   açık state machine + idempotency + outbox ile — dağıtık transaction YOK.
3. **Sınırlar modül kontratlarıdır.** Bugün iki modül aynı transaction'ı paylaşabilir;
   ancak bu "modül sınırı yok" demek değildir. Her paylaşım bilinçli ve dokümantedir.

## Transaction Boundary Tablosu

| Use Case | İlgili Modüller | Transaction Kapsamı | Neden | Mekanizma |
|---|---|---|---|---|
| Receive inventory (giriş) | Inbound + Inventory | Tek tx | Balance + ledger atomik; yarım giriş olmaz | Lokal DB tx + idempotency anahtarı (ReceiptId+LineNo) |
| Putaway (RCV→storage) | Inbound + Inventory | Tek tx | İki balance satırı + ledger atomik | Lokal tx; warehouse-total invariant'ı test edilir |
| Location→Location move | Inventory | Tek tx | Kaynak −, hedef + aynı anda | Lokal tx + invariant testi |
| **Allocate (sipariş)** | Outbound + Inventory | Tek tx | Outbound sipariş satırı (referans) + Inventory reservation/sayaç atomik; oversell imkânsız | **Atomik koşullu UPDATE** (aşağıda) + reservation — tek yazar Inventory (ADR-0006) |
| Pick confirm | Outbound + Inventory | Tek tx | reservation → CONSUMED, quantity −, allocated − aynı anda | Koşullu update + idempotency (PickTaskId) |
| Transfer Ship | Transfers + Outbound + Inventory | Tek tx (kaynak taraf) | State geçişi + TransferOut + outbox event atomik | Outbox pattern |
| Transfer Receive | Transfers + Inbound + Inventory | Tek tx (hedef taraf) | State geçişi + TransferIn atomik | Outbox pattern |
| Sourcing (Fulfillment) | Fulfillment | Okuma (mutasyon yok) | Karar üretilir, stok rezerve edilmez | Read-only sorgu + decision persist |
| OMS → FulfillmentOrder | Integration + Fulfillment + Outbound | Tek tx | Duplicate sipariş işlenmemeli | UNIQUE(oms_order_id) + idempotency |

**Kilit nokta — neden aynı tx?** Aynı veritabanındaki iki modülün verisini tek atomik adımda
güncelleyebilmek, "iki kaynak atomik olmalı" invariant'ları için doğru araçtır. **Dağıtılmış
olduğumuzda bu tx'ler iki faza/saga'ya dönüşecektir** — o gün ilgili kontrat çağrıları
mesaja çevrilir, idempotency anahtarları zaten yerindedir. Bu yüzden tx paylaşımı "modül
tablosuna direkt erişim" değildir: her modül yalnızca kendi tablosunu yazar, karşı modülün
kontratını çağırır; tx yalnızca unit-of-work kapsamıdır.

## Concurrency Stratejisi

### 1. Atomik Koşullu UPDATE (allocation — race-proof)

```sql
-- Allocate(q) — tek atomik adım; race condition imkânsız
UPDATE inventory.inventory_balance
SET    allocated = allocated + @q
WHERE  warehouse_id = @w AND location_id = @l AND sku_id = @s
  AND  status = 'AVAILABLE'                -- ← allocation yalnız uygun kovaya
  AND  quantity - allocated >= @q;         -- ← koşul DB'de değerlendirilir

-- etkilenen satır = 0  →  InsufficientInventory (domain hatası, 500 değil)
-- etkilenen satır = 1  →  başarılı; allocation belgesiyle aynı tx
```

`if (available > 0) { ... }` tarzı application-level kontrol **kullanılmaz** — koşul verinin
değerlendirildiği yerde (DB) atomik uygulanır. Kabul testi: `Available=1`, iki eşzamanlı
allocation → tam olarak biri başarılı, `allocated` asla 2 olamaz.

### 2. Optimistic Concurrency (move / adjust / status değişimi)

`row_version` (version kolonu) ile: okuma → değişiklik → `UPDATE ... WHERE row_version = @okunan`.
Çakışma → `ConcurrencyConflict` → yeniden dene (idempotent komutlarda) veya kullanıcıya bildir.

### 3. DB Constraint'ler (invariant'ların son kalesi)

| Constraint | Tablo | Amaç |
|---|---|---|
| `quantity >= 0` | inventory_balance | Negatif stok imkânsız |
| `allocated >= 0 AND allocated <= quantity` | inventory_balance | Talep fiziksel stoğu aşamaz |
| `status='AVAILABLE' OR allocated=0` | inventory_balance | Talep yalnız satılabilir stoğa |
| UNIQUE (warehouse_id, location_id, sku_id, status) | inventory_balance | Kova başına tek satır |
| UNIQUE (warehouse_id, code) | facility.location | Kod çakışması imkânsız |
| UNIQUE (reference_type, reference_id, line_no) | inventory_transaction | Çift uygulama imkânsız (idempotency) |
| CHECK (shipped <= allocated <= requested) | transfers.transfer_line | Transfer muhasebe zinciri |
| CHECK (received_qty >= 0) | transfers.transfer_line | **Üst sınır bilinçli olarak domain kuralı** — discrepancy akışları schema değişikliği gerektirmesin |

Uygulama kodu invariant'ı ilk savunma hattı, DB constraint son savunma hattıdır — ikisi birden vardır.

## Cross-Module Referans Bütünlüğü (FK'sız dünyada)

Çapraz modül FK yoktur (ADR-0001). Bütünlük şu stratejiyle korunur:

1. **Yazma zamanı doğrulama:** her yazma komutu kontrat girişinde referans verdiği
   (location, SKU, warehouse) varlığı **sahibinin lookup kontratı** üzerinden doğrular —
   yoksa/uygun değilse domain hatası döner. Bu, uygulama seviyesi FK'dır.
2. **Hard delete yasak:** Facility/MasterData satırları silinmez, deactivate/retire edilir
   → tarihsel referanslar daima çözülebilir; ledger'da ID + code snapshot'ı tutulur.
3. **Lifecycle koordinasyonu:** "stoklu location retire edilemez" kuralı, modül DAG'ını
   bozmamak için API seviyesi koordinasyon use-case'iyle (Facility + Inventory kontratları)
   uygulanır — Facility → Inventory bağımlılığı kurulmaz.
4. **Orphan tespiti:** periyodik uzlaşma sorguları kırık referansları yakalar ve yönetim
   ekranında görünür kılar (anormallik görünürlüğü).
5. **Kontrat testleri:** her yazma yolunun referans doğrulamasını integration testiyle kilitler.

Detay: [MULTI_WAREHOUSE_ISOLATION.md](MULTI_WAREHOUSE_ISOLATION.md).

## Eventual Consistency Bölgeleri

| Bölge | Tutarlılık | Gerekçe / Mekanizma |
|---|---|---|
| Transfer workflow | Eventual | Fiziksel süreç günler sürer; state machine + InTransit + idempotency |
| Network projeksiyon (Phase 11/13+) | Eventual | Event-beslemeli; staleness görünür kılınır (son işlenen event id / timestamp) |
| Dış entegrasyon (OMS, carrier) | Eventual | Outbox + inbox + idempotent consumer |

## Outbox / Inbox (mesajlaşma geldiğinde — INTEGRATION.md)

```text
[Business State + Modül-own Outbox Row] aynı tx'de commit → relay broker'a gönderir
→ consumer: Inbox (processed event ids) ile duplicate koruması
```

Kural: **asla** "önce DB commit, sonra mesaj publish" — arada çökerse mesaj kaybolur.
Outbox tablosu her modülün kendi şemasındadır (Integration'a bağımlılık oluşmaz).

## Anormallik Görünürlüğü (§72)

Sistemde tutarsızlık olursa sessizce gizlenmez:

- `Available < 0` **imkânsızdır** (koşullu update + constraint) — oluşursa alarm kriteridir.
- Balance ↔ ledger uzlaşma kontrolü (validasyon/uzlaşma query'si): periyodik kontrol eder,
  sapma varsa yönetim ekranında görünür.
- Transfer uyuşmazlığı (shipped ≠ received, açık InTransit pozisyonu, zaman aşımı) →
  transfer dashboard'unda "problemli transferler" listesi.
- Deaktif location'da kalan stok, orphan referanslar → uzlaşma ekranında.

## Test Haritası

| Seviye | Örnekler |
|---|---|
| Unit | Negatif stok oluşturulamaz; invalid transfer transition atılamaz; blokajlı location'a putaway red; HOLD/QUARANTINE/DAMAGED'a allocation red |
| Integration | Allocation concurrency (N paralel istek → tek başarı); UNIQUE/CHECK constraint'ler; outbox aynı tx'de yazılır; FK'sız referans doğrulaması her yazma yolunda |
| E2E | Warehouse kur → location kur → SKU → receive → putaway → allocate → pick → pack → ship; A→B transfer senaryosu (transfer-op nötrlüğü); yetki senaryosu |
