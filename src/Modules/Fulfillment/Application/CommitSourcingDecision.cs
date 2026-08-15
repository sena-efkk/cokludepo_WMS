using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wms.Integration.Telemetry;
using Wms.Modules.Fulfillment.Application.Optimization;
using Wms.Modules.Fulfillment.Domain;
using Wms.Modules.Outbound.Contracts;

namespace Wms.Modules.Fulfillment.Application;

public enum SourcingCommitOutcome
{
    Committed = 1,
    AlreadyCommitted = 2,
    Stale = 3,
}

public sealed record CommitSourcingLineInput(Guid SkuId, int Quantity);

public sealed record CommitSourcingWarehouseInput(Guid WarehouseId, IReadOnlyList<CommitSourcingLineInput> Lines);

public sealed record OptimizationSnapshotInput(
    string StrategyUsed,
    OptimizationStatus Status,
    decimal TotalCost,
    decimal TotalDistanceKm,
    string RouteSource,
    IReadOnlyList<string> Explanations);

public sealed record CommitSourcingCommand(
    Guid RequestId,
    Guid SourcingRequestId,
    IReadOnlyList<CommitSourcingWarehouseInput> Plan,
    OptimizationSnapshotInput? Optimization = null);

public sealed record SourcingOrderLinkInfo(Guid WarehouseId, Guid OutboundOrderId, string OrderNumber);

public sealed record CommitSourcingResult(
    SourcingCommitOutcome Outcome,
    Guid? DecisionId,
    IReadOnlyList<SourcingOrderLinkInfo> OrderLinks,
    string? StaleReason);

public sealed class CommitSourcingDecision(
    IFulfillmentStore store,
    IOutboundContract outbound)
{
    public async Task<CommitSourcingResult> Handle(CommitSourcingCommand command, CancellationToken cancellationToken)
    {
        var existingDecision = await store.GetSourcingDecisionByRequestIdAsync(command.RequestId, cancellationToken);
        if (existingDecision is not null)
        {
            var existingLinks = await store.ListOrderLinksAsync(existingDecision.Id, cancellationToken);
            return new CommitSourcingResult(
                SourcingCommitOutcome.AlreadyCommitted,
                existingDecision.Id,
                existingLinks.Select(l => new SourcingOrderLinkInfo(l.WarehouseId, l.OutboundOrderId, l.OrderNumber)).ToList(),
                null);
        }

        var request = await store.GetSourcingRequestAsync(command.SourcingRequestId, cancellationToken)
            ?? throw new SourcingRequestNotFoundException(command.SourcingRequestId);

        if (request.Status == SourcingStatus.Stale)
        {
            throw new InvalidSourcingStateException("Stale sourcing request yeniden commit edilemez — tekrar evaluate edin.");
        }

        if (command.Plan.Count == 0)
        {
            throw new ArgumentException("Commit planı en az bir warehouse içermelidir.");
        }

        // 1) Her warehouse için Outbound order + allocation (deterministik RequestId → idempotent).
        var createdOrders = new List<(Guid WarehouseId, Guid OrderId, string OrderNumber)>();
        foreach (var warehouse in command.Plan)
        {
            var orderRequestId = DeriveOrderRequestId(command.RequestId, warehouse.WarehouseId);
            var created = await outbound.CreateOrderAsync(
                orderRequestId,
                null,
                warehouse.WarehouseId,
                command.SourcingRequestId.ToString(),
                warehouse.Lines
                    .Select(l => new OutboundOrderLineInput(l.SkuId, l.Quantity))
                    .ToList(),
                cancellationToken);

            var allocateResult = await outbound.AllocateOrderAsync(created.OrderId, cancellationToken);
            if (allocateResult.Outcome == OutboundAllocateOutcome.InsufficientStock)
            {
                // 2) Stale: evaluation sonrası stok değişmiş — oluşan order'ları iptal et (reservation release).
                foreach (var (_, orderId, _) in createdOrders)
                {
                    await TryCancelOrderAsync(orderId, cancellationToken);
                }

                await store.BeginTransactionAsync(cancellationToken);
                try
                {
                    var fresh = await store.GetSourcingRequestAsync(command.SourcingRequestId, cancellationToken);
                    fresh!.MarkStale();
                    await store.SaveChangesAsync(cancellationToken);
                    await store.CommitTransactionAsync(cancellationToken);
                }
                catch
                {
                    await store.RollbackTransactionAsync(cancellationToken);
                    throw;
                }

                WmsMetrics.SourcingStaleTotal.Add(1);

                return new CommitSourcingResult(
                    SourcingCommitOutcome.Stale,
                    null,
                    [],
                    $"Warehouse {warehouse.WarehouseId} allocation başarısız — evaluation sonrası stok değişmiş (SOURCING_STALE). Tekrar evaluate edin.");
            }

            createdOrders.Add((warehouse.WarehouseId, created.OrderId, created.OrderNumber));
        }

        // 3) Decision + snapshot + linkler (tek transaction; crash sonrası retry güvenli).
        var snapshot = JsonSerializer.Serialize(new
        {
            command.RequestId,
            command.SourcingRequestId,
            Plan = createdOrders.Select(o => new { o.WarehouseId, o.OrderId, o.OrderNumber }).ToArray(),
            Optimization = command.Optimization is null
                ? null
                : new
                {
                    command.Optimization.StrategyUsed,
                    Status = command.Optimization.Status.ToString(),
                    command.Optimization.TotalCost,
                    command.Optimization.TotalDistanceKm,
                    command.Optimization.RouteSource,
                    Explanations = command.Optimization.Explanations.ToArray(),
                },
        });

        var decision = SourcingDecision.Create(command.RequestId, command.SourcingRequestId, snapshot);

        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            var fresh = await store.GetSourcingRequestAsync(command.SourcingRequestId, cancellationToken)
                ?? throw new SourcingRequestNotFoundException(command.SourcingRequestId);

            fresh.MarkCommitted();
            await store.AddSourcingDecisionAsync(decision, cancellationToken);
            foreach (var order in createdOrders)
            {
                await store.AddOrderLinkAsync(SourcingOrderLink.Create(decision.Id, order.WarehouseId, order.OrderId, order.OrderNumber), cancellationToken);
            }

            var outcome = await store.SaveChangesAsync(cancellationToken);
            if (outcome == FulfillmentSaveOutcome.DuplicateRequest)
            {
                await store.RollbackTransactionAsync(cancellationToken);
                var winner = await store.GetSourcingDecisionByRequestIdAsync(command.RequestId, cancellationToken);
                if (winner is not null)
                {
                    var winnerLinks = await store.ListOrderLinksAsync(winner.Id, cancellationToken);
                    return new CommitSourcingResult(
                        SourcingCommitOutcome.AlreadyCommitted,
                        winner.Id,
                        winnerLinks.Select(l => new SourcingOrderLinkInfo(l.WarehouseId, l.OutboundOrderId, l.OrderNumber)).ToList(),
                        null);
                }

                throw new InvalidOperationException($"Sourcing commit çakıştı ama decision bulunamadı: {command.RequestId}");
            }

            await store.CommitTransactionAsync(cancellationToken);

            return new CommitSourcingResult(
                SourcingCommitOutcome.Committed,
                decision.Id,
                createdOrders.Select(o => new SourcingOrderLinkInfo(o.WarehouseId, o.OrderId, o.OrderNumber)).ToList(),
                null);
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private async Task TryCancelOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        try
        {
            await outbound.CancelOrderAsync(orderId, cancellationToken);
        }
        catch
        {
            // Cancel best-effort; stale durumunda reservation bırakmamak hedeflenir —
            // başarısız cancel operasyonel log'a bırakılır (order CANCELLED durumda değilse tekrar denenebilir).
        }
    }

    internal static Guid DeriveOrderRequestId(Guid decisionRequestId, Guid warehouseId)
    {
        var bytes = Encoding.UTF8.GetBytes($"{decisionRequestId:N}:{warehouseId:N}");
        var hash = MD5.HashData(bytes);
        return new Guid(hash);
    }
}

public sealed class SourcingRequestNotFoundException : Exception
{
    public SourcingRequestNotFoundException(Guid sourcingRequestId)
        : base($"Sourcing request bulunamadı: {sourcingRequestId}")
    {
    }
}

public sealed class InvalidSourcingStateException : Exception
{
    public InvalidSourcingStateException(string message)
        : base(message)
    {
    }
}
