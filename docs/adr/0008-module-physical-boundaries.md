# ADR-0008: Module Physical Boundary Strategy (tek csproj / katmanlar namespace seviyesi) + Architecture Test Mekanizması

- **Tarih:** 2026-08-13 (Phase 3)
- **Durum:** Accepted

## Context

Phase 2'de 8 business module + Administration + Wms.Api (composition root) tasarlandı.
Phase 3'te bunları fiziksel .NET projelerine dönüştürürken iki seçenek vardı:

1. Modül başına 3-4 proje (Domain/Application/Infrastructure/Contracts) → 32+ csproj.
2. Modül başına tek proje, katman ayrımı klasör + namespace seviyesinde → 10 csproj.

Ayrıca architecture testleri için araç seçimi ve makineye özgü bir kısıt (Windows
Application Control) karara bağlanmalıydı.

## Decision

### 1. Modül başına TEK csproj (toplam 10)

- 8 business module + Administration: `Wms.Modules.<Name>` class library'leri, net10.0.
- Katmanlar (`Domain/`, `Application/`, `Infrastructure/`) klasör + namespace seviyesinde:
  `Wms.Modules.<Name>.Domain` / `.Application` / `.Infrastructure`.
- `Integration` business bounded context değildir → proje YOK (Phase 13'te teknik sınır
  olarak gelecek; INTEGRATION.md).
- SharedKernel/Common/Core projesi YOK — ortak soyutlama gerçek ihtiyaçla doğar.
- `Wms.Api` hiçbir modülü referanslamaz (YAGNI); referanslar modül registration'ı
  gerektiren fazda eklenir. Test projesi tüm projeleri referanslar (kural denetçisi).

### 2. Modül başına tek assembly marker

Her modülde bir marker tip: `public static class <Module>Module;` (modül kök namespace'inde).
Amaç: assembly'nin tip taşıması + ileride registration/type-scanning için tutarlı nokta.
Domain/Application/Infrastructure için ayrı marker YOK (boş dosya üretilmez).

### 3. Architecture test mekanizması: Mono.Cecil ile metadata inspection

İlk tercih ArchUnitNET idi (flutent kural API'si, MIT). Ancak makinedeki **Windows
Application Control (0x800711C7)** politikası taze derlenen imzasız assembly'lerin
**runtime yüklenmesini** engelledi (`Assembly.LoadFrom` dahil). ArchUnitNET `ArchLoader`
runtime `Assembly` nesnesi gerektirdiğinden çalışamadı.

**Karar:** Kurallar değişmedi, taşıyıcı değişti — testler assembly'leri **Mono.Cecil ile
disk üzerinden metadata olarak okur** (runtime yükleme yok, politika tetiklenmez).
Cecil zaten ArchUnitNET'in kendi analiz motorudur; namespace/assembly referansı + tip
seviyesi IL bağımlılık taraması elde yazılan küçük bir inspector ile yapılır
(`AssemblyInspector`, `TypeReferenceScanner`).

### 4. Korunan kurallar (Phase 3'te aktif)

1. Modül kataloğu: tam 8 beklenen modül; `Wms.Modules.Integration` henüz YOK.
2. Modüller `Wms.Api` assembly'sini referanslamaz; modül tip'leri `Wms.Api` namespace'ine bağımlı olmaz.
3. Modül→modül assembly referansları Phase 2 DAG'inin izinli kenarlarına ⊆ olmalı (şu an 0 kenar).
4. İzinli DAG kenarları bilinen modüllere işaret eder ve cycle içermez.
5. `Wms.Modules.*.Domain` namespace'leri hiçbir `*.Infrastructure` namespace'ine bağımlı olmaz (vakuöz — ilk Domain tipiyle aktifleşir).
6. `*.Domain` framework namespace'lerine (`Microsoft.AspNetCore*`, `Microsoft.EntityFrameworkCore*`, `Microsoft.Extensions*`) bağımlı olmaz (vakuöz — ilk Domain tipiyle aktifleşir).

## Alternatives

| Alternatif | Neden reddedildi |
|---|---|
| Katman başına ayrı csproj (project explosion) | 32+ proje; ilk foundation için seremonisiz; gerçek isolation ihtiyacında bölünür |
| Contracts projelerini şimdiden üretmek | YAGNI — kontratlar business kodla doğar; boş interface seti üretilmez |
| ArchUnitNET'e devam (runtime load) | Windows Application Control nedeniyle çalışmıyor; policy değiştirilemez |
| Saf reflection (`GetReferencedAssemblies`) ile yetinmek | Tip/namespace seviyesi kurallar (Domain→Infrastructure) IL taraması gerektirir; Cecil şart |
| NetArchTest | ArchUnitNET ile aynı runtime-load kısıtına takılır; ekstra paket gereği yok |

## Consequences

- ✅ 10 csproj; sınırlar fiziksel olarak görünür, disiplin test ile zorunlu.
- ✅ İleride modül servis olarak ayrılacaksa: namespace→proje taşıma mekanik bir refactor;
  DAG kenarları zaten test datası olarak hazır.
- ⚠️ Katman disiplini derleyici değil test ile korunur → mimari testler CI'da zorunlu olmalı.
- ⚠️ Testler Debug çıktıları okur; bin klasöründeki bayat DLL riski → `dotnet test` önce
  build yapar (varsayılan davranış) — risk sınırlı.
- ⚠️ Kurallar 5-6 henüz vakuöz (Domain tipi yok); Phase 5+ ilk Domain koduyla aktifleşir.
