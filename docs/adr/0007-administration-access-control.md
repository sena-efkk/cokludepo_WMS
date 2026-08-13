# ADR-0007: Administration Modülü — Authentication Altyapısı ile Warehouse-Scope Access Control Ayrımı

- **Tarih:** 2026-08-13 (Validation Gate)
- **Durum:** Accepted

## Context

Sistem warehouse-scope yetki gerektirir (§31): Bursa WarehouseManager İstanbul'da mutation
yapamamalı. Modül listesinde kullanıcı/erişim yönetimi için domain yoktu. "Kullanıcı kim?"
(authN) ile "hangi warehouse üzerinde ne yapabilir?" (authZ) aynı katmana gömülürse erişim
kuralları controller'lara dağılır ve sahipsiz kalır.

## Decision

1. **Administration ayrı bir domain-support modülüdür**; kullanıcı, rol, warehouse erişim
   atamaları ve audit log'un sahibidir.
2. **Authentication ("kim?") = teknik altyapı:** ASP.NET Core Identity (self-hosted,
   ücretsiz) + JWT. Identity yalnızca kimlik doğrular.
3. **Authorization / Access Control ("hangi warehouse üzerinde hangi işlem?") = Administration'ın
   domain/application sorumluluğu.** Warehouse-scope erişim kuralı bir domain kararıdır:
   - `user_warehouse_access (user_id, warehouse_id, role)` — UNIQUE (user, warehouse);
     erişim kümesinin **writable truth'u**.
   - Login'de bu küme claim'lere çevrilir; tüm warehouse-scope kararlar bu claim'lerden türetilir
     (request body'den DEĞİL).
   - Network-scope roller (NetworkManager, SystemAdmin) warehouse satırı taşımaz; küme = tüm depolar.
4. **Enforcement noktası:** API/presentation katmanında cross-cutting (policy + mutation guard +
   query guard), Administration'ın `IAccessControl` kontratı üzerinden. **Domain modülleri
   Administration'a bağımlı OLMAZ** — modül DAG'ı korunur.
5. Warehouse-scope mutation guard: endpoint'teki `WarehouseId` ∈ erişim kümesi; query guard:
   sorgular otomatik daraltılır. İhlal → `WarehouseAccessDenied` (403, domain hatası modeliyle).
6. Audit: erişim ataması değişiklikleri de dahil kritik operasyonlar `audit_log`'a yazılır
   (stok ledger'ından ayrı bir kavramdır).

## Alternatives

| Alternatif | Neden reddedildi |
|---|---|
| Identity + erişim tabloları API'ye gömülü (modül yok) | "Kim hangi depoya erişebilir" domain'i sahipsiz; kurallar dağılır |
| Her modülde kendi enforcement | Tekrar eden kod; kural tutarlılığı zor; modüller Authorization'a bağımlı olur |
| Ayrı mikro kimlik servisi | Öğrenci projesi için operasyonel yük; monolit içinde gerek yok |
| Warehouse-scope bilgiyi request body'den almak | Kullanıcı kendi erişim kümesini genişletemez ama body'ye yazabilir — güvenlik açığı |

## Consequences

- ✅ Domain modülleri temiz kalır (Administration bağımlılığı yok); DAG bozulmaz.
- ✅ Erişim kümesinin tek writable truth'u vardır; değişiklikler audit'lenir.
- ⚠️ Enforcement cross-cutting olduğundan "guard unutulması" riski vardır →
  warehouse-scope endpoint'lerin tamamı için integration testleri zorunludur
  (erişimsiz depo verisi sızmamalı; mutation 403 dönmeli).
- ⚠️ JWT vs cookie kararı Phase 3'te netleşir (öneri: JWT, API-first).
