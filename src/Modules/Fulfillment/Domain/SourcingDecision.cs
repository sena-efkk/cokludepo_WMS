namespace Wms.Modules.Fulfillment.Domain;

public sealed class SourcingDecision : IHasTimestamps
{
    private SourcingDecision()
    {
        PlanSnapshot = string.Empty;
    }

    private SourcingDecision(
        Guid requestId,
        Guid sourcingRequestId,
        string planSnapshot,
        DateTime committedAt)
    {
        Id = Guid.NewGuid();
        RequestId = requestId;
        SourcingRequestId = sourcingRequestId;
        PlanSnapshot = planSnapshot;
        CommittedAt = committedAt;
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid SourcingRequestId { get; private set; }

    public string PlanSnapshot { get; private set; }

    public DateTime CommittedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    public static SourcingDecision Create(
        Guid requestId,
        Guid sourcingRequestId,
        string planSnapshot,
        DateTime? committedAt = null)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Sourcing decision bir RequestId taşımalıdır.", nameof(requestId));
        }

        if (sourcingRequestId == Guid.Empty)
        {
            throw new ArgumentException("Sourcing decision bir request'e bağlı olmalıdır.", nameof(sourcingRequestId));
        }

        if (string.IsNullOrWhiteSpace(planSnapshot))
        {
            throw new ArgumentException("Sourcing decision plan snapshot taşımalıdır.", nameof(planSnapshot));
        }

        return new SourcingDecision(requestId, sourcingRequestId, planSnapshot, committedAt ?? DateTime.UtcNow);
    }
}

public sealed class SourcingOrderLink
{
    private SourcingOrderLink()
    {
        OrderNumber = string.Empty;
    }

    private SourcingOrderLink(Guid decisionId, Guid warehouseId, Guid outboundOrderId, string orderNumber)
    {
        Id = Guid.NewGuid();
        DecisionId = decisionId;
        WarehouseId = warehouseId;
        OutboundOrderId = outboundOrderId;
        OrderNumber = orderNumber;
    }

    public Guid Id { get; private set; }

    public Guid DecisionId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid OutboundOrderId { get; private set; }

    public string OrderNumber { get; private set; }

    public static SourcingOrderLink Create(Guid decisionId, Guid warehouseId, Guid outboundOrderId, string orderNumber)
    {
        if (decisionId == Guid.Empty || warehouseId == Guid.Empty || outboundOrderId == Guid.Empty)
        {
            throw new ArgumentException("Sourcing order link; decision, warehouse ve order zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            throw new ArgumentException("Sourcing order link order number taşımalıdır.", nameof(orderNumber));
        }

        return new SourcingOrderLink(decisionId, warehouseId, outboundOrderId, orderNumber.Trim().ToUpperInvariant());
    }
}
