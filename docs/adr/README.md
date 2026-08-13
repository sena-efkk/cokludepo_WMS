# Architecture Decision Records

Bu dizin projenin önemli mimari kararlarını kaydeder. Her ADR şu bölümleri içerir:
**Context, Decision, Alternatives, Consequences**.

| # | Karar | Tarih | Durum |
|---|---|---|---|
| [0001](0001-modular-monolith.md) | Modular Monolith + modül şemaları (çapraz FK yok) + in-process kontratlar | 2026-08-13 | Accepted (rev. Validation Gate) |
| [0002](0002-generic-location-hierarchy.md) | Generic hiyerarşik Location modeli (rigid Zone→Aisle→Rack zinciri yerine) | 2026-08-13 | Accepted |
| [0003](0003-inventory-balance-and-ledger.md) | Durum bölmeli InventoryBalance + append-only ledger + kesin status/ATP semantiği | 2026-08-13 | Accepted (rev. Validation Gate) |
| [0004](0004-transfer-eventual-consistency.md) | Transfer = explicit workflow, eventual consistency; transfer-op nötrlüğü; discrepancy kapanışı | 2026-08-13 | Accepted (rev. Validation Gate) |
| [0005](0005-network-inventory-projection.md) | Network görünümü MVP'de canlı agregasyon; event projeksiyonu ertelendi | 2026-08-13 | Accepted |
| [0006](0006-location-level-allocation.md) | Location seviyesi allocation + atomik koşullu UPDATE | 2026-08-13 | Accepted |
| [0007](0007-administration-access-control.md) | Administration modülü: authN (Identity) ile warehouse-scope authZ (domain) ayrımı | 2026-08-13 | Accepted |
| [0008](0008-module-physical-boundaries.md) | Module physical boundaries: tek csproj/modül + Mono.Cecil arch testleri (WDAC kısıtı) | 2026-08-13 | Accepted |
| [0009](0009-persistence-foundation.md) | Persistence foundation: tek connection, schema-per-module, migration sahipliği modülde, EF ertelendi | 2026-08-13 | Accepted |
