using System.Diagnostics.Metrics;

namespace Wms.Integration.Telemetry;

/// <summary>
/// Merkezi metric tanımları — Prometheus convention, düşük kardinaliteli label'lar
/// (SkuId/OrderId gibi yüksek kardinaliteli değerler label YAPILMAZ).
/// </summary>
public static class WmsMetrics
{
    public static readonly Meter Meter = new("Wms.Metrics", "1.0");

    // Inventory
    public static readonly Counter<long> InventoryReservationsTotal = Meter.CreateCounter<long>("inventory_reservations_total");
    public static readonly Counter<long> InventoryReservationFailuresTotal = Meter.CreateCounter<long>("inventory_reservation_failures_total");
    public static readonly Counter<long> InventoryMovementsTotal = Meter.CreateCounter<long>("inventory_movements_total");
    public static readonly Counter<long> InventoryReceivesTotal = Meter.CreateCounter<long>("inventory_receives_total");
    public static readonly Counter<long> InventoryAdjustmentsTotal = Meter.CreateCounter<long>("inventory_adjustments_total");
    public static readonly Counter<long> PickNotFoundTotal = Meter.CreateCounter<long>("pick_not_found_total");
    public static readonly Counter<long> CycleCountsCreatedTotal = Meter.CreateCounter<long>("cycle_counts_created_total");
    public static readonly Counter<long> ReconciliationsTotal = Meter.CreateCounter<long>("reconciliations_total");

    // Outbound / Inbound
    public static readonly Counter<long> OrdersCreatedTotal = Meter.CreateCounter<long>("orders_created_total");
    public static readonly Counter<long> OrdersShippedTotal = Meter.CreateCounter<long>("orders_shipped_total");
    public static readonly Counter<long> AllocationFailuresTotal = Meter.CreateCounter<long>("allocation_failures_total");
    public static readonly Counter<long> PickFailuresTotal = Meter.CreateCounter<long>("pick_failures_total");
    public static readonly Counter<long> ReceiptsTotal = Meter.CreateCounter<long>("receipts_total");
    public static readonly Counter<long> ReceivingDiscrepanciesTotal = Meter.CreateCounter<long>("receiving_discrepancies_total");

    // Messaging
    public static readonly Counter<long> OutboxPublishFailuresTotal = Meter.CreateCounter<long>("outbox_publish_failures_total");
    public static readonly Counter<long> ConsumerFailuresTotal = Meter.CreateCounter<long>("consumer_failures_total");
    public static readonly Counter<long> DlqMessagesTotal = Meter.CreateCounter<long>("dlq_messages_total");

    private static long _outboxPending;
    private static double _outboxOldestPendingSeconds;

    public static void SetOutboxPending(long pending, double oldestPendingSeconds)
    {
        Interlocked.Exchange(ref _outboxPending, pending);
        Volatile.Write(ref _outboxOldestPendingSeconds, oldestPendingSeconds);
    }

    public static readonly ObservableGauge<long> OutboxPending = Meter.CreateObservableGauge(
        "outbox_pending",
        () => Interlocked.Read(ref _outboxPending));

    public static readonly ObservableGauge<double> OutboxOldestPendingSeconds = Meter.CreateObservableGauge(
        "outbox_oldest_pending_seconds",
        () => Volatile.Read(ref _outboxOldestPendingSeconds));

    // Sourcing / Optimization
    public static readonly Counter<long> SourcingRequestsTotal = Meter.CreateCounter<long>("sourcing_requests_total");
    public static readonly Counter<long> SourcingStaleTotal = Meter.CreateCounter<long>("sourcing_stale_total");
    public static readonly Counter<long> SplitPlanTotal = Meter.CreateCounter<long>("split_plan_total");
    public static readonly Counter<long> OptimizationFallbackTotal = Meter.CreateCounter<long>("optimization_fallback_total");
    public static readonly Counter<long> RoutingFallbackTotal = Meter.CreateCounter<long>("routing_fallback_total");
    public static readonly Histogram<double> SourcingDuration = Meter.CreateHistogram<double>("sourcing_duration_seconds");
    public static readonly Histogram<double> RoutingDuration = Meter.CreateHistogram<double>("routing_duration_seconds");
    public static readonly Histogram<double> OptimizationDuration = Meter.CreateHistogram<double>("optimization_duration_seconds");

    public static readonly ObservableGauge<double> SourcingCandidateCount = Meter.CreateObservableGauge(
        "sourcing_candidate_count",
        () => Volatile.Read(ref _sourcingCandidateCount));

    private static double _sourcingCandidateCount;

    public static void SetSourcingCandidateCount(double count) => Volatile.Write(ref _sourcingCandidateCount, count);
}
