# Demo Kılavuzu

> Hedef: sistemi Docker'da ayağa kaldırıp web UI üzerinden 5 senaryoyu
> uçtan uca yürütmek. Tüm adımlar gerçek backend use case'lerini kullanır —
> doğrudan SQL/seed yok (tek istisna: şema migration'ları).

## 1. Başlatma

```bash
cd deploy
copy .env.example .env      # POSTGRES_PASSWORD, RABBITMQ_PASSWORD vb. doldur
docker compose up -d --build
```

| Bileşen | Adres | Not |
|---|---|---|
| Web UI | http://localhost:5173 | nginx; `/api` → wms-api proxy |
| API | http://localhost:8080 | `/health`, Prometheus `/metrics` |
| Grafana | http://localhost:3000 | anonim viewer; WMS dashboard'u |
| RabbitMQ mgmt | http://localhost:15672 | integration mesajları |
| PostgreSQL | localhost:5432 | schema-per-module |

API başlangıçta 7 modül şemasının migration'larını sırayla uygular
(`DbMigrator`); fresh volume'de hiçbir manuel adım gerekmez.

## 2. Demo Verisi

UI → **Scenarios** sayfasında 6 senaryo kartı vardır; her biri
`POST /api/dev/scenarios/{scenario}/initialize` çağırır. Senaryo kurulumu
idempotent'tir (SKU/location code'a göre var olanı yeniden yaratmaz).

### Scenario 0 — Synthetic Dataset (önce bunu çalıştırın)

3 depo (Bursa / İstanbul / İnegöl) + 12 SKU + location hiyerarşisi
(RECEIVING → A01..A03 → bin'ler + STORAGE) + stok + her depoda açık bir
receipt (putaway bekleyen) kurar.

Kurulum sonrası tur:

- **Overview**: network rollup (depo başına SKU/stok), risk özeti.
- **Warehouses**: depo listesi → depoya gir → location tree (parent-child),
  ATP ve lokasyon kapasiteleri.
- **Inventory**: SKU arama → Network → Warehouse → Location drill-down;
  ledger (her hareketin append-only kaydı).
- **Inbound**: açık receipt'ler görünür; bir receipt'i receive edip
  putaway task'ını tamamlayın (scan'ler: location code + SKU barcode +
  destination code — UI hazır doldurur).
- **Accuracy**: risk panosu; putaway sonrası ledger'a yansıması.

## 3. Senaryolar

### Scenario 1 — Normal Fulfillment

1. `Initialize`.
2. **Inbound**: receipt → receive → putaway (stok bin'e girdi).
3. **Sourcing**: SKU + quantity + destination (Bursa) → Evaluate
   (optimized strateji) → Commit. Aday depo skorları, açıklamalar ve
   counterfactual'lar görünür.
4. **Outbound**: commit'in yarattığı order görünür → pick task'ları
   confirm → pack → ship.
5. **Inventory**: ledger'da IN→OUT hareketleri; ATP düştü.

### Scenario 2 — Phantom Inventory

1. `Initialize`.
2. **Outbound**: iki farklı order oluştur, allocate et, pick task'ında
   **Not Found** işaretle (sistem 5 görüyor, fizikselde yok).
3. **Accuracy**: risk assessment RED'e döner (tekrarlı PickNotFound).
4. **Accuracy**: Cycle Count — evaluate → queue'daki task → Start →
   blind count 0 → VarianceDetected → Reconciliation oluşur.
5. **Accuracy**: Reconciliation → Approve → adjustment ledger'a yazılır;
   **Inventory** sayfasında ATP düzeldi.

### Scenario 3 — Warehouse Transfer

1. `Initialize`.
2. **Transfers**: A (Bursa) → B (İstanbul) transfer oluştur → allocate →
   ship (InTransit).
3. **Transfers**: partial receive (1/2) → InTransit muhasebesi düşer.
4. Final receive → Completed; destination depoda stok belirir.

### Scenario 4 — Fragmented Inventory

1. `Initialize`.
2. **Sourcing**: strateji `compare` → Nearest vs Greedy vs Optimized üç
   plan yan yana; split yapan planlarda penalty açıklaması.
3. Recommended strateji ile Commit.

### Scenario 5 — Sourcing Race (hata akışı)

1. `Initialize` + stok kur.
2. **Sourcing**: evaluate → ATP=1 plan görünür.
3. Evaluate'i commit'lemeden stoğu başka bir işlemle reserve et
   (Outbound order + allocate).
4. **Sourcing**: Commit → `409 SOURCING_STALE` ve açıklama UI'da görünür.

## 4. E2E Kabul Testleri

Docker stack ayağa kalkmışken yerel makinede:

```bash
cd apps/wms-simulator-web
npx playwright install chromium
# API 127.0.0.1:5217'de çalışmalı (veya webServer ayarı):
npx playwright test
```

6 test (sıralı): Receive→Putaway→Inventory, Order→Ship, NotFound→CycleCount→
Reconciliation, Transfer partial receive, Sourcing compare, SOURCING_STALE.
Gerçek PostgreSQL + RabbitMQ üzerinde koşar; benzersiz SKU'lar üretir.

## 5. İzleme

- API metrics: http://localhost:8080/metrics (OpenTelemetry + Prometheus exporter).
- Grafana: Prometheus datasource hazır; `wms-api` process/HTTP/inventory
  metrikleri dashboard'larda.
- RabbitMQ: outbox dispatcher exchange'leri (`wms-integration`), retry/DLX.
