# Observability & Operations (Phase 16)

## Stack (ücretsiz / self-hosted)

```text
Wms.Api (OpenTelemetry SDK)
   │  /metrics (Prometheus exporter — pull)
   ▼
Prometheus (docker, scrape 10s)
   ▼
Grafana (provisioned dashboard "WMS Operations", 12 panel)

PostgreSQL + RabbitMQ + (optional) OSRM
```

Tracing: OTel SDK kurulu; trace EXPORTER'ı bilinçli olarak eklenmedi (backend yokken
OTLP exporter ölü ağırlık + paket advisory'si). Bir collector/Tempo geldiğinde
`AddOtlpExporter` tek satırla eklenir. Log aggregation: structured console logging
(bkz. aşağıda) — Loki gerçek ihtiyaçla (OPERATIONS kararı).

## Health Endpoints

- `GET /health` — application + postgresql + rabbitmq + osrm.
  - OSRM **optional** bağımlılık: kapalıyken `Degraded` (Haversine fallback aktif) — WMS DOWN olmaz.
  - Liveness/readiness ayrımı: `/health` bütünleşik; production ayrımı deployment evresine bırakıldı.
- `GET /metrics` — Prometheus scrape endpoint.

## Metrikler (düşük kardinaliteli; SkuId/OrderId label DEĞİL)

| Alan | Metrikler |
|---|---|
| System | `http_server_request_duration_seconds`, `http_server_active_requests` |
| Inventory | `inventory_reservations_total`, `inventory_reservation_failures_total`, `inventory_movements_total`, `inventory_receives_total`, `inventory_adjustments_total`, `pick_not_found_total`, `cycle_counts_created_total`, `reconciliations_total` |
| Outbound/Inbound | `orders_created_total`, `orders_shipped_total`, `allocation_failures_total`, `pick_failures_total`, `receipts_total`, `receiving_discrepancies_total` |
| Messaging | `outbox_pending`, `outbox_oldest_pending_seconds`, `outbox_publish_failures_total`, `consumer_failures_total`, `dlq_messages_total` |
| Sourcing | `sourcing_requests_total`, `sourcing_stale_total`, `sourcing_candidate_count`, `sourcing_duration_seconds`, `optimization_duration_seconds`, `split_plan_total`, `optimization_fallback_total`, `routing_fallback_total` |

## Trace Correlation

Business akışta `RequestId / OrderId / EventId / CorrelationId` structured log property
olarak taşınır (messaging envelope: EventId + CorrelationId). Module'lar kendi ayrı
correlation standardı ÜRETMEZ — mevcut request/event id'leri kullanılır.

## Structured Logging Kuralları

- Context property olarak: OrderId, WarehouseId, SkuId, ReservationId, TransferId, EventId, CorrelationId, RequestId.
- Secret/password/token LOGLANMAZ; payload'ın tamamı kontrolsüz loglanmaz (outbox dispatcher yalnız EventType/EventId/error mesajı yazar).

## Outbox Retention

`OutboxRetentionService`: YALNIZ `published + N gün` yaşlı kayıtları siler
(`Outbox:RetentionDays`, default 30; 0 = kapalı). Pending/failed kayıtlara ASLA dokunulmaz.

## Common Failure Scenarios (görünürlük)

| Durum | Log | Metrik |
|---|---|---|
| RabbitMQ unavailable | dispatcher warning (retry metadata) | `outbox_pending` ↑, `outbox_oldest_pending_seconds` ↑ |
| Outbox backlog | — | `outbox_pending` ↑ |
| DLQ mesajı | consumer error (redelivered → DLQ) | `dlq_messages_total` ↑ |
| Reservation contention | InsufficientInventory (409) | `inventory_reservation_failures_total` ↑ |
| Sourcing stale | SOURCING_STALE response | `sourcing_stale_total` ↑ |
| OSRM down | route fallback (HAVERSINE_FALLBACK) | `routing_fallback_total` ↑ |
| Optimizer timeout | GREEDY_FALLBACK status | `optimization_fallback_total` ↑ |
| Cycle count discrepancy | reconciliation case | `reconciliations_total` ↑ |

## Security / Config Sanity

- Gerçek secret'lar yalnız `deploy/.env` (gitignored). `appsettings.json` yalnız yerel dev
  default'ları içerir (RabbitMQ "wms/wms-dev-password" — dev-only; production env override).
- `.env.example` boş şablon. Repo'da commit edilmiş secret YOK (kontrol edildi).
- NuGet audit: OpenTelemetry.Api GHSA-g94r-2vxg-569j advisory'si için patched sürüm henüz
  yayınlanmadı — `Directory.Build.props` içinde `NuGetAuditSuppress` + gerekçe (local dev,
  external attack surface yok). Patched sürüm çıkınca kaldırılacak.

## Başlatma (OPERATIONS)

```bash
# 1) Altyapı (postgres + rabbitmq + prometheus + grafana)
copy deploy/.env.example deploy/.env   # değerleri doldur
docker compose -f deploy/docker-compose.yml up -d

# 2) API (host)
$env:ConnectionStrings__WmsDatabase = "Host=localhost;Port=5432;Database=<db>;Username=<user>;Password=<pass>"
$env:RabbitMQ__Username = "<user>"
$env:RabbitMQ__Password = "<pass>"
dotnet run --project src/Wms.Api

# 3) Erişim
#  API        : http://localhost:5217  (health: /health, metrics: /metrics)
#  Prometheus : http://localhost:9090
#  Grafana    : http://localhost:3000  (anonymous viewer; dashboard: WMS Operations)
#  RabbitMQ   : http://localhost:15672 (management)
```

OSRM opsiyoneldir (`Fulfillment:Optimization:OsrmBaseUrl`); kapalıyken Haversine fallback.

## Architecture Snapshot (Phase 16 sonu)

```text
Clients (HTTP)
   │
   ▼
Wms.Api (composition root: endpoints + health + /metrics)
   │
   ├── Modules: MasterData · Facility · Inventory · Inbound · Outbound · Transfers · Fulfillment
   │      └── PostgreSQL (schema-per-module; cross-module FK yok)
   │
   ├── Wms.Integration (teknik): Outbox/Inbox + dispatcher + consumer + telemetry
   │      └── RabbitMQ (wms-integration exchange + DLX/DLQ)
   │
   └── Background Workers: OutboxDispatcher · IntegrationConsumer · OutboxRetention
          │
          ▼
   Telemetry: OpenTelemetry → Prometheus → Grafana
```
