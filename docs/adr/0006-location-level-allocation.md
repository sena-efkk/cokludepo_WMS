# ADR-0006: Allocation State Tek Sahibi Inventory — Location Seviyesi Reservation + Atomik Koşullu UPDATE

- **Tarih:** 2026-08-13 (rev. 2 — Validation Gate: joint ownership kaldırıldı)
- **Durum:** Accepted

## Context

E-ticarette aynı stoka eşzamanlı talepler olur (`Available=1`, Order A ve Order B aynı anda
1'er ister). İkisinin de başarılı olması kabul edilemez. "Hangi depo" kararı Fulfillment'ta,
"hangi bin'den" kararı ise operasyonel gerçektir — sipariş anında bin seçilmezse pick
sırasında stok kavgası doğar.

İlk revizyonda allocation "belge Outbound'da, sayaç Inventory'de" (joint) olarak
modellenmişti. Bu iki yazarlı bir truth'tu: belge ile sayaç senkron sapabilir, reservation
lifecycle'ı iki modüle bölünür. **Joint ownership kaldırıldı.**

## Decision

1. **Allocation state'inin tek writable sahibi Inventory'dir:**
   - `inventory.inventory_reservation`: id, warehouse_id, sku_id, location_id, quantity,
     type (ORDER | TRANSFER), reference (sipariş satırı / transfer satırı), state
     (ALLOCATED → CONSUMED | RELEASED), created_at, actor_id.
   - `inventory_balance.allocated` sayacı — reservation'ın fast-path toplamı.
   - İkisi de **Inventory'nin aynı transaction'ında** değişir; tek yazar.
2. **Outbound allocation state'i YAZMAZ.** Sipariş satırı için
   `IInventoryContract.Allocate(orderLineRef, skuId, warehouseId, qty)` çağırır.
   Inventory location-level seçim yapar (MVP: basit deterministic strateji — Phase 10;
   Phase 15'te strategy abstraction), reservation'ı oluşturur, sayacı **atomik koşullu
   UPDATE** ile artırır ve **reservation id döner**:

   ```sql
   UPDATE inventory.inventory_balance
   SET allocated = allocated + @q
   WHERE warehouse_id=@w AND location_id=@l AND sku_id=@s
     AND status='AVAILABLE' AND quantity - allocated >= @q;
   ```

   etkilenen satır 0 → `InsufficientInventory` (domain hatası). Kontrol uygulama kodunda
   `if (available > q)` ile değil, koşulun verinin yaşadığı yerde (DB) atomik değerlendirilmesiyle yapılır.
3. **Outbound, reservation id'yi kendi workflow belgesinde referans olarak saklar**
   (fulfillment order line üzerinde) — bu bir truth kopyası değildir; işaretçidir.
   Bugün monolith'te sipariş satırı + reservation aynı DB tx'inde yazılabilir; dağıtıklaşınca
   reservation önden yapılır (saga adımı), Outbound yalnızca id taşır.
4. **Pick confirm → `Consume(reservationId)`**: Inventory tek tx'inde
   `quantity -= q`, `allocated -= q`, reservation → CONSUMED. Idempotent (PickTaskId).
5. **`Deallocate` / iptal**: Outbound `Deallocate(reservationId)` çağırır; Inventory
   `allocated -= q` ve reservation → RELEASED. Çift çağrı idempotent.
6. Move/Adjust/ChangeStatus için optimistic concurrency (`row_version`).

## Alternatives

| Alternatif | Neden reddedildi |
|---|---|
| Joint ownership (belge Outbound, sayaç Inventory) | İki yazar tek kavram; belge/sayaç senkron sapması; lifecycle ikiye bölünür |
| Outbound'ın reservation tablosunu yazması | Çapraz modül tablo mutasyonu — ownership kuralını deler |
| Warehouse seviyesi allocation | Pick anında bin kavgası; ikinci bir rezervasyon uzlaşma katmanı gerekir |
| Application-level `if available > 0` kontrolü | Race condition; iki istek aynı anda geçer (acceptance testi yakalar) |
| Pessimistic lock (SELECT FOR UPDATE) her allocasyonda | Monolith'te gerek yok; satır kitlenmesi contention yaratır |
| Available'ı saklayıp azaltmak | Duplicate truth; balance ile sapma riski |

## Consequences

- ✅ Oversell **imkânsız** — koşul DB'de atomik; acceptance testi N paralel istekle kanıtlanır.
- ✅ Allocation state'inin **tek yazarı** var; reservation lifecycle (ALLOCATED/CONSUMED/RELEASED)
  tek yerde yönetilir; "hayalet allocated" yapısal olarak zorlaşır.
- ✅ Outbound basit kalır: talep et → id al → kendi akışını yürüt.
- ⚠️ Location-seçim stratejisi Inventory içinde bir variation point'tir →
  Phase 10'da basit deterministik seçim, Phase 15'te strategy abstraction.
- ⚠️ Outbound'ın reservation id'siz "yetim reservation" üretmemesi için iptal akışında
  Deallocate zorunlu — integration testleriyle korunur.
