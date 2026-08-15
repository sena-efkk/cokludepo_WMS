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

## Yarın: RabbitMQ Ne Zaman, Nerede (Phase 13 ✅)

Broker yalnızca **gerçek process sınırı** varsa devreye girer:

| Sınır | Örnek | Zamanlama |
|---|---|---|
| Dış sistem entegrasyonu | OMS → FulfillmentOrder (async), carrier status | İhtiyaçla |
| Projeksiyon besleme | Inventory events → NetworkInventoryProjection | Phase 11/13 |
| Process ayrımı | Warehouse Runtime A/B/C (hedef topoloji) | Deployment evrimi |
| Network event'leri | Transfer state değişimleri merkezi görünüme | İhtiyaçla |

Monolit içi senkron modül çağrıları **broker'a taşınmaz** — gerek yoktur, yalnızca gecikme
ve karmaşıklık ekler.

## Implementation Status (Phase 13 — Transactional Outbox / Idempotent Consumers)

### Akış

```text
OUTBOUND TRANSACTION
Shipment → SHIPPED  +  Outbox(ShipmentShipped)   [AYNI PG transaction]
        │
        ▼ COMMIT
Outbox Dispatcher (BackgroundService, 3 sn poll, batch 50)
        │
        ▼
RabbitMQ (wms-integration exchange, routing key = event-type.v1)
        │
        ▼
Consumer Queue (transfers-inbox; DLX → transfers-inbox-dlq)
        │
        ▼
Inbox Idempotency (UNIQUE(consumer, event_id))
        │
        ▼
Transfer Handler (idempotent business op)
```

Prensipler:

> Database commit and event intent must be atomic.
> Delivery may happen more than once; business effect must not.
> RabbitMQ is transport, not source of truth.
> Outbox prevents event loss; Inbox prevents duplicate processing.

### Üretilen Event'ler (explicit DTO — domain dump YOK)

| Event | Routing key | Producer | Consumer |
|---|---|---|---|
| `ShipmentShippedV1` | `outbound.shipment-shipped.v1` | Outbound (ShipOrder tx) | Transfers |
| `ReceiptCompletedV1` | `inbound.receipt-completed.v1` | Inbound (receipt completion tx) | Transfers |

Envelope: `EventId, EventType, EventVersion, OccurredAt, CorrelationId, Payload(JSON)`.
EventId stable (shipment id / receipt id) — retry yeni EventId üretmez.

### Yapı Taşları

- `Wms.Integration` — TEKNİK assembly (business bounded context DEĞİL): envelope + event DTO'ları,
  `OutboxMessage` (AttemptCount/LastError/NextAttemptAt), `InboxMessage`, `IRabbitMqPublisher`
  (topology + DLX/DLQ declare), `OutboxDispatcher(+Service)`, `IntegrationConsumerService`
  (manual ack; 1 redelivery sonrası DLQ), `IIntegrationConsumer`.
- Outbox tabloları MODÜL-OWN (ADR-0009): `outbound.outbox_message`, `inbound.outbox_message` —
  business state ile aynı local tx. Inbox: `transfers.inbox_message` (UNIQUE(consumer, event_id)).
- Retry: exponential-ish backoff (5s → 30s → 5dk), event ASLA silinmez; cleanup policy
  (published > N gün) ileride — şimdilik retention dökümante.
- RabbitMQ: `wms-integration` direct exchange + `wms-integration-dlx` + DLQ'lar; topology
  declare idempotent; config `RabbitMQ:Host/Port/Username/Password` (appsettings default +
  env override; secret repo'da YOK).
- `/health` → rabbitmq connectivity check eklendi. Public publish endpoint YOK.

### Transfer Entegrasyonu

- `ShipmentShipped` → transfer (OutboundOrderId korelasyonu) → `ShipTransfer` idempotent
  tetiklenir (event yalnız tetikleyici; business op kendi deterministik RequestId'leriyle retry-safe).
- `ReceiptCompleted` → transfer (InboundReceiptId korelasyonu) → tüm line'lar kapalıysa
  COMPLETED (domain guard; duplicate event → inbox skip).
- Event path CANLI doğrulandı: transfer ship API'si çağrılmadan, outbound ship →
  outbox → RabbitMQ → consumer → transfer `IN_TRANSIT` (+ destination receipt).

### Testler (14 yeni — gerçek PostgreSQL + gerçek RabbitMQ)

Atomicity (business+outbox aynı tx; rollback → outbox yok), Shipment/Receipt outbox payload
korelasyonu, dispatcher publish + published tekrar dispatch edilmez, broker-down (pending +
attempt/error metadata, business kaybolmaz), broker recovery (pending → gerçek kuyruk),
duplicate delivery → tek business effect (inbox + idempotent handler, ShipmentShipped ve
ReceiptCompleted için), DLQ poison yakalar, unknown event graceful ignore, contract DTO
primitive-only, event direction (producer→consumer assembly kanıtı), RabbitMQ container
healthcheck (management API), arch süiti. Tam süit 332/332.

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
