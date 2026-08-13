# ADR-0001: Modular Monolith + Modül Şemaları + In-Process Kontratlar

- **Tarih:** 2026-08-13 (rev. 2026-08-13 — Validation Gate)
- **Durum:** Accepted

## Context

Öğrenci projesi; ilk günden 20 microservice, Kubernetes veya Kafka cluster istemiyoruz.
Ancak sistem multi-warehouse ve gelecekte modüllerin ayrı servislere çıkabilmesi isteniyor.
Depolar birbirine bağlı ama bağımlı olmamalı; modül sahipliği net olmalı; bugünkü kararlar
yarınki dağıtıma engel olmamalı.

## Decision

1. **Tek deploy edilebilir: Modular Monolith** (ASP.NET Core + PostgreSQL). Her modül
   ayrı proje, kendi Domain/Application/Infrastructure katmanıyla.
2. **Modül başına PostgreSQL şeması** (`master_data`, `facility`, `inventory`, `inbound`,
   `outbound`, `transfers`, `fulfillment`, `administration`, `integration`), her modülün
   kendi DbContext'i. Modül içi FK'lar **kullanılır**.
3. **Çapraz modül foreign key YOK.** Modüller birbirini `Guid` ID ile referanslar;
   bütünlük stratejisi bu ADR'nin "Sonuçları" bölümünde ve MULTI_WAREHOUSE_ISOLATION.md'de
   açıkça tanımlanmıştır.
4. **Çapraz modül iletişim = in-process uygulama kontratları** (modül owner'ı tanımlar,
   consumer çağırır). Broker (RabbitMQ) yalnızca gerçek process sınırında devreye girer.
5. Dependency yönü tek yönlü DAG (edge tipleriyle MODULE_MAP.md'de): MasterData ve Facility
   kök; Inventory bunlara; Inbound/Outbound Inventory'ye; Transfers Inbound/Outbound'a;
   Fulfillment okuma tarafı. Cyclic dependency yasak; architecture testleriyle CI'da enforce edilir.
6. **Integration bir business bounded context DEĞİLDİR** — teknik sınırdır (adapter'ler,
   outbox relay, inbox transport). Event kontratları business modüllerindir; outbox tabloları
   modül-own'dur (her modül kendi şemasına yazar).

## Alternatives

| Alternatif | Neden reddedildi |
|---|---|
| İlk günden microservice'ler | Öğrenci projesi için operasyonel yük; domain öğrenme hedefini gölgeler |
| Tek schema, tüm tablolar public | Ownership DB'de görünmez; ayrışma sinyali yok |
| Çapraz modül FK'lar | Şemaları/DB'leri fiziksel kenetler; ileride DB bölünmesini kısıt ihlalleriyle engeller |
| Modüller arası her iletişim için RabbitMQ | Monolit içinde gereksiz gecikme + karmaşıklık; outbox zorunluluğu boşuna |
| Tek dev proje (klasör bazlı modül) | Derleme seviyesinde bağımlılık kontrolü yok; sınır ihlali kolay |

## Consequences

- ✅ Modül sınırları compile-time görünür; sızıntılar arch testiyle yakalanır.
- ✅ İleride DB/process ayrıştırması: şema bölme kararına indirgenir, FK kısıtı engel olmaz.
- ⚠️ **FK'sız referans bütünlüğü bir disiplin meselesidir; stratejisi açık:**
  1. **Yazma zamanı doğrulama**: her yazma komutu kontrat girişinde referans verdiği
     varlığı sahibinin lookup kontratıyla doğrular (uygulama seviyesi FK) — geçersiz → domain hatası.
  2. **Hard delete yasak**: Facility/MasterData satırları silinmez; deactivate/retire.
     Tarihsel referanslar (ledger vb.) daima çözülebilir; ledger'a ID + code snapshot yazılır.
  3. **Lifecycle koordinasyonu**: "stoklu location retire edilemez" kuralı API seviyesi
     koordinasyon use-case'iyle uygulanır (Facility → Inventory bağımlılığı kurulmaz).
  4. **Orphan tespiti**: periyodik uzlaşma sorguları + yönetim ekranında görünürlük.
  5. **Kontrat testleri**: her yazma yolunun referans doğrulaması integration testiyle kilitlenir.
- ⚠️ Aynı DB'de iki modül aynı transaction'ı paylaşabilir; bu bilinçli olmalı ve dokümante
  edilmeli (CONSISTENCY.md) — dağıtıklaşınca bu tx'ler saga'ya dönüşür, idempotency anahtarları
  şimdiden yerleşik olmalı.
