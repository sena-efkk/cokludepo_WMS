# ADR-0004: Transfer = Explicit Workflow (Eventual Consistency), InTransit Transfers'ta

- **Tarih:** 2026-08-13 (rev. 2026-08-13 — Validation Gate)
- **Durum:** Accepted

## Context

Warehouse-to-Warehouse transfer tek stok hareketi değildir: kaynak depoda outbound akışı,
fiziksel taşıma, hedef depoda inbound akışı içerir ve saatler/günler sürebilir. Stok bu
süreç boyunca izlenebilir kalmalıdır.

## Decision

1. **TransferOrder + TransferLine** (Transfers modülü, network scope) ve açık **state
   machine**: Requested → Approved → SourceAllocated → Picking → Packed → Shipped →
   InTransit → Receiving → Received → Putaway → Completed (iptal yalnız ship öncesi;
   allocation'dan iptalde release).
2. **InTransit pozisyonu = `shipped_qty − received_qty − variance_qty`** — TransferLine'dan
   türetilir; ayrı yazılabilir tablo YOK. MVP'de `variance_qty = 0`.
3. Stok etkileri Inventory'ye **Outbound (kaynak) ve Inbound (hedef) kontratları**
   üzerinden uygulanır; Transfers stok mutate etmez.
4. Her adım kendi transaction'ında; **iki depoyu saran distributed transaction YOK** —
   workflow doğası gereği eventually consistent.
5. Her state geçişi `transfer_event` üretir (timeline UI + audit + problem görünürlüğü).
6. Adımlar idempotent: `(reference_type, reference_id, line_no)` UNIQUE anahtarları.
7. **Terminal kuralı:** `status ∈ {Completed, Cancelled}` ⇒ InTransit = 0.

## Invariant'ın Kesin Tanımı (Validation Gate ile daraltıldı)

> **Transfer-op nötrlüğü:** Bir transferin kendi muhasebe kapsamında — kaynak çıkışı
> (−shipped), InTransit pozisyonu ve hedef girişi (+received) — sistem geneli stok toplamına
> net etki her adımda **0**'dır. Transfer stoku yaratmaz/yok etmez, yerini değiştirir.

- Bu **global network inventory invariant DEĞİLDİR**. Receiving, customer shipment, damage,
  adjustment, disposal network toplamını meşru olarak değiştirir.
- "Network total sabittir" ifadesi yalnızca transfer lifecycle'ı bağlamında anlamlıdır;
  dokümanlardaki genel-geçer okumalar düzeltilmiştir.

## Discrepancy Stratejisi (model engellemez)

MVP'de discrepancy workflow implement edilmez; kural: `received == shipped` değilse transfer
`Receiving` state'inde kalır ve problemli transferler listesinde görünür. `received_qty`
üzerinde DB CHECK **yoktur** (yalnız `>= 0`) — ileride schema değişikliği olmadan:

| Senaryo | Çözüm |
|---|---|
| Short receipt | `variance_qty = fark` (reason: miscount) → InTransit kapanır, network −fark (kayıtlı) |
| Lost in transit | `variance_qty = fark` (reason: LOST) → kapanır, claim kaydı |
| Damaged in transit | Hedef depoya **DAMAGED** kovasında TransferIn → toplam korunur |
| Over receipt | Aşan kısım ayrı adjustment + overage kaydı |
| Reconciliation | Periyodik uzlaşma + manuel resolution |

**Kapanış kuralı:** terminal state'e geçiş ancak InTransit = 0 iken mümkündür; açık pozisyon
problem listesinde yaşar — "ortada kaybolmuş stok" sessiz kalamaz.

## Alternatives

| Alternatif | Neden reddedildi |
|---|---|
| `Inventory.Move(A, B)` olarak modellemek | Fiziksel süreci, ara durumları, iptal/onay akışını görünmez kılar |
| İki DB transaction'ını aynı anda commit (2PC) | Günler süren fiziksel akışta anlamsız; kilitler ve kısıtlar zararlı |
| InTransit'i ayrı bir tabloya yazmak (source of truth) | Çift yazılabilir truth; shipped/received ile senkron sapması riski |
| Şişirilmiş tek-entity akış (her şey TransferOrder içinde) | Outbound/Inbound becerileri kopyalanır; bağımlılık kuralı bozulur |

## Consequences

- ✅ Transfer-op nötrlüğü her adımda korunur ve otomatik test edilir (Phase 12).
- ✅ Transfer izlenebilir: timeline + correlation id ile tüm adımlar.
- ✅ Discrepancy genişletmesi schema değişikliği gerektirmez; terminal kapanış kuralı hazır.
- ⚠️ Eventual consistency disiplini gerekir: state makinesi tek geçiş noktası olmalı,
  race'ler idempotency anahtarlarıyla engellenir.
- ⚠️ MVP'de exact receipt zorunludur — gerçek operasyonlarda ilk discrepancy'de problem
  listesi büyüyebilir; resolution akışı Phase 12 sonrası öncelikli adaydır.
