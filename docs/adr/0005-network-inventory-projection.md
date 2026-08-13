# ADR-0005: Network Görünümü MVP'de Canlı Agregasyon; Event Projeksiyonu Ertelendi

- **Tarih:** 2026-08-13
- **Durum:** Accepted

## Context

Network-level karar mekanizması (Fulfillment) her karar için tüm location kayıtlarını
okumamalı; SKU bazında Network → Warehouse → Location drill-down görünümü gerekiyor.
Aynı zamanda "duplicate mutable truth üretme" ilkesi var: stokun tek yazılabilir gerçeği
location balance'lardır.

## Decision

1. MVP'de **ayrı NetworkInventoryProjection tablosu YOKTUR**. Warehouse toplamları ve
   network toplamları, location balance'lar üzerinden index'li SQL agregasyonuyla anlık
   hesaplanır (canlı okuma).
2. **Network görünümü iki AYRI kavramdır — karıştırılmaz:**
   - **NetworkPhysicalStock** = `Σ warehouse OnHand + Σ InTransit` — stokun fiziksel olarak
     ağın neresinde olduğunun görünümü (depolarda + yolda).
   - **NetworkAvailableToPromise (ATP)** = `Σ warehouse Available` — **InTransit DAHİL DEĞİL (MVP)**.
     Yoldaki stok, sipariş sözü verilebilir stok olarak sayılmaz. InTransit'in ATP'ye
     dahil edilip edilmeyeceği (ör. transfer ETA/SLA'ya göre promise) ileride bir
     **business policy** kararıdır — MVP'de implement edilmez, kavramlar yalnızca ayrık tutulur.
3. Event-beslemeli projeksiyon (Inventory events → projection tablosu) **gerçek ihtiyaç
   oluştuğunda** (Phase 11/13: sorgu maliyeti, process ayrımı, broker entegrasyonu) eklenir.
   O zaman: eventual consistency, staleness görünürlüğü (son işlenen event), rebuild yolu,
   idempotent consumer — hepsi planlıdır (INTEGRATION.md).

## Alternatives

| Alternatif | Neden reddedildi |
|---|---|
| İlk günden event-beslemeli projeksiyon | Monolit MVP için erken karmaşıklık; gecikmeli tutarsız okuma riski; broker olmadan anlamı sınırlı |
| Warehouse toplamlarını ayrı tabloya yazmak | İkinci yazılabilir stok gerçeği; sapma riski (denormalize truth anti-pattern) |
| Fulfillment'ın location-level sorgu yapması | Modül sınırı ihlali; sorgu maliyeti ve ownership bulanıklığı |

## Consequences

- ✅ Tek yazılabilir truth korunur; network görünümü her zaman tutarlı (anlık).
- ✅ NetworkPhysicalStock ile NetworkATP ayrı metriklerdir; InTransit yalnızca fiziksel
  görünüme girer — "yoldaki stok satılabilir sanıldı" hatası yapısal olarak önlenir.
- ✅ Kod yüzeyi küçük: read-model sorguları Inventory kontratı üzerinden.
- ⚠️ Okuma maliyeti veri büyüyünce artar → önce index/query optimizasyonu, sonra projeksiyon
  (ölçümle, erken optimizasyon yok).
- ⚠️ Projeksiyon geldiğinde eventual consistency kuralları devreye girer — dokümante ve
  test edilmeli.
