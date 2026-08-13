using Wms.Modules.Inventory.Domain;

namespace Wms.Modules.Inventory.Domain.Accuracy.CycleCounting;

public sealed class CycleCountResult
{
    private CycleCountResult()
    {
    }

    private CycleCountResult(
        Guid cycleCountTaskId,
        int countedQuantity,
        string? countedBy,
        DateTime countedAt,
        int expectedQuantity,
        int expectedAllocated,
        InventoryStatus expectedStatus,
        int variance,
        CountOutcome outcome)
    {
        Id = Guid.NewGuid();
        CycleCountTaskId = cycleCountTaskId;
        CountedQuantity = countedQuantity;
        CountedBy = countedBy;
        CountedAt = countedAt;
        ExpectedQuantity = expectedQuantity;
        ExpectedAllocated = expectedAllocated;
        ExpectedStatus = expectedStatus;
        Variance = variance;
        Outcome = outcome;
    }

    public Guid Id { get; private set; }

    public Guid CycleCountTaskId { get; private set; }

    public int CountedQuantity { get; private set; }

    public string? CountedBy { get; private set; }

    public DateTime CountedAt { get; private set; }

    public int ExpectedQuantity { get; private set; }

    public int ExpectedAllocated { get; private set; }

    public InventoryStatus ExpectedStatus { get; private set; }

    public int Variance { get; private set; }

    public CountOutcome Outcome { get; private set; }

    public bool RequiresReconciliation => Outcome == CountOutcome.VarianceDetected;

    public static CycleCountResult Create(
        Guid cycleCountTaskId,
        int countedQuantity,
        string? countedBy,
        DateTime countedAt,
        int expectedQuantity,
        int expectedAllocated,
        InventoryStatus expectedStatus,
        int variance,
        CountOutcome outcome)
    {
        if (cycleCountTaskId == Guid.Empty)
        {
            throw new ArgumentException("Result bir CycleCountTask'a bağlı olmalıdır.", nameof(cycleCountTaskId));
        }

        if (countedQuantity < 0 || expectedQuantity < 0 || expectedAllocated < 0)
        {
            throw new ArgumentException("Sayım ve beklenen miktarlar negatif olamaz.");
        }

        return new CycleCountResult(
            cycleCountTaskId,
            countedQuantity,
            countedBy,
            countedAt,
            expectedQuantity,
            expectedAllocated,
            expectedStatus,
            variance,
            outcome);
    }
}
