# Integration & Messaging Strategy

> Bugün: modular monolith içinde **in-process kontratlar** + domain event'ler.
> Yarın (gerçek sınır oluştuğunda): RabbitMQ + Outbox/Inbox.
> "Microservice-ready olmak, microservice olmak değildir" — sınırlar hazır, broker ertelenmiştir.

## Integration'ın Konumu (teknik sınır, business domain değil)

Integration **business bounded context DEĞİLDİR** — MasterData/Inventory/Transfers ile aynı
anlamda domain mantığı taşımaz. Sorumlulukları yalnızca:

1. **Dış sistem adaptörleri**: OMS (FulfillmentOrder girişi), supplier (ASN), carrier
   (shipment durumu). Adapter bir mesajı tercüme eder ve ilgili **business modülün kontratını**
   çağırır — karar vermez.
2. **Outbox relay'i**: her modülün **kendi şemasındaki** outbox tablosunu okuyup broker'a
   gönderen worker (zamanı gelince — Phase 13).
3. **Inbox mekanizması**: gelen mesajların idempotent işlenmesi (processed EventId'ler).
4. **Event transport**: (ileride) RabbitMQ bağlantısı, retry, DLQ.

**Event kontratlarının sahibi business modüllerdir**: `inventory.StockChanged.v1` Inventory'nin,
`transfers.TransferShipped.v1` Transfers'ın kontratıdır. Integration bunları yalnızca taşır.
Outbox **tablosu** da modül-own'dur (ör. `inventory.outbox`) — böylece business modüller
Integration'a bağımlı olmaz (DAG korunur, bkz. [MODULE_MAP.md](MODULE_MAP.md)).

## Bugün: In-Process İletişim (MVP)

Aynı process içindeki modül iletişimi için **broker kullanılmaz**. İki araç vardır:

### 1. Uygulama Kontratları (senkron, komut/query)

Modül dışına açılan tek kapı. Owner modülde tanımlanır, consumer çağırır:

```text
IInventoryContract.Receive / Move / Adjust / Allocate / Deallocate / Consume / TransferOut / TransferIn
IOutboundContract.BeginTransferShipment
IInboundContract.BeginTransferReceiving
ISourcingService.EvaluateCandidates
```

- Mediator kütüphanesi YOKTUR (şimdilik) — §41 gereği her bağımlılık sorgulanır;
  basit in-process dispatch elle yazılır. İhtiyaç doğarsa değerlendirilir.

### 2. Domain Event'ler (modül içi + sınırlı çapraz)

Modül içindeki yan etkiler ve **okuma tarafı** bildirimleri için:

```text
Örn: Outbound → ShipmentPicked / ShipmentShipped (Transfers dinler, state ilerletir)
     Inventory → StockChanged (ileride projeksiyon besleyecek)
```

Basit in-process event dispatcher (built-in veya küçük bir abstraction). Event'ler
outbox-ready biçimde tasarlanır: `EventId, AggregateId, CorrelationId, OccurredAt, Payload`.

## Yarın: RabbitMQ Ne Zaman, Nerede (Phase 13)

Broker yalnızca **gerçek process sınırı** varsa devreye girer:

| Sınır | Örnek | Zamanlama |
|---|---|---|
| Dış sistem entegrasyonu | OMS → FulfillmentOrder (async), carrier status | İhtiyaçla |
| Projeksiyon besleme | Inventory events → NetworkInventoryProjection | Phase 11/13 |
| Process ayrımı | Warehouse Runtime A/B/C (hedef topoloji) | Deployment evrimi |
| Network event'leri | Transfer state değişimleri merkezi görünüme | İhtiyaçla |

Monolit içi senkron modül çağrıları **broker'a taşınmaz** — gerek yoktur, yalnızca gecikme
ve karmaşıklık ekler.

## Outbox Pattern (zamanı gelince — ADR-0001'de taahhüt)

```text
[Business State + Modül-own Outbox Row] aynı DB tx'de commit
        │
        ▼ (Integration relay worker, polling veya NOTIFY)
RabbitMQ publish
        ▼
Consumer: Inbox tablosu (işlenen EventId'ler, UNIQUE) → duplicate koruması
```

- **Asla** "DB commit ettikten sonra ayrı adımda publish" — ikisi atomik olmalı.
- Consumer **idempotent** olmak zorunda; `EventId` tekrar işlemeyi engeller.
- Retry + DLQ (dead letter queue) politika: geçici hatalar retry, kalıcı hatalar DLQ +
  görünür anomali (bkz. [CONSISTENCY.md](CONSISTENCY.md)).

## Event Kontrat Tasarımı (bugünden hazırlanır)

```json
{
  "eventId": "uuid",
  "eventType": "inventory.StockChanged.v1",     // geçmiş zaman adı, sürüm
  "correlationId": "uuid",
  "causationId": "uuid",
  "occurredAt": "iso-8601",
  "aggregateType": "InventoryBalance",
  "aggregateId": "uuid",
  "payload": { ... }
}
```

Kurallar:

- Event adları **geçmiş zamanda**, modül önekli, sürümlü: `inventory.StockChanged.v1`,
  `transfers.TransferShipped.v1`.
- Sürümleme: breaking değişim → yeni `v2` event; eski sürüm bir süre üretilmeye devam eder.
- Correlation ID her akışta taşınır (sipariş → sourcing → allocation → pick → ship tek iz).
- Payload'da başka modülün entity'sinin dump'ı YOK — ihtiyaç duyulan asgari projeksiyon verisi.

## Dış Entegrasyon Yüzeyleri

| Dış Sistem | İlk uygulama | Sonra |
|---|---|---|
| OMS (E-Commerce) | REST endpoint: `POST /api/fulfillment-orders` (idempotent, `oms_order_id` UNIQUE) | Webhook / RabbitMQ (outbox) |
| Tedarikçi | ASN import (dosya/REST) → InboundShipment | EDI (ileride, gerekirse) |
| Carrier | Shipment durum güncelleme endpoint'i (REST) | Webhook → event |
| Rota (OSRM) | `IRouteProvider` abstraction → `OsrmRouteProvider` (ücretsiz, self-host) | opsiyonel `GoogleRouteProvider` adapter |

> Ücretli/cloud servis **zorunlu bağımlılık olmaz**; provider abstraction varsa bile
> default implementasyon ücretsiz/self-hosted olandır (OSRM, OSM, RabbitMQ, Prometheus, Grafana).

## Kırmızı Çizgiler

- ❌ Modüller arası her etkileşim için RabbitMQ kullanmak (monolit içinde broker yok).
- ❌ Event payload'ına tam entity gömmek (kontrat şişer, sürümleme kırılır).
- ❌ "Fire and forget" yazıp idempotency'yi atlamak — her consumer idempotent olmak zorunda.
- ❌ DB commit sonrası ayrı publish (outbox'suz) — mesaj kaybı riski kabul edilmez.
- ❌ Integration'a domain mantığı koymak — Integration transport/adaptördür, karar vermez.
