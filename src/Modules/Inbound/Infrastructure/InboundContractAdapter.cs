using Wms.Modules.Inbound.Application;
using Wms.Modules.Inbound.Contracts;
using Wms.Integration.Telemetry;
using Wms.Modules.Inbound.Domain;

namespace Wms.Modules.Inbound.Infrastructure;

public sealed class InboundContractAdapter(
    CreateReceipt createReceipt,
    GetReceipt getReceipt,
    ReceiveItems receiveItems) : IInboundContract
{
    public async Task<InboundReceiptCreated> CreateReceiptAsync(
        Guid requestId,
        string? receiptNumber,
        Guid warehouseId,
        string? externalReference,
        string? sourceType,
        IReadOnlyList<InboundReceiptLineInput> lines,
        CancellationToken cancellationToken)
    {
        var result = await createReceipt.Handle(
            new CreateReceiptCommand(
                requestId,
                receiptNumber,
                warehouseId,
                externalReference,
                sourceType,
                lines.Select(l => new CreateReceiptLineInput(l.SkuId, l.ExpectedQuantity)).ToList()),
            cancellationToken);
        WmsMetrics.ReceiptsTotal.Add(1);
        return new InboundReceiptCreated(result.ReceiptId, result.ReceiptNumber);
    }

    public async Task<InboundReceiptInfo?> GetReceiptAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await getReceipt.Handle(receiptId, cancellationToken);
        return receipt is null
            ? null
            : new InboundReceiptInfo(
                receipt.Id,
                receipt.ReceiptNumber,
                receipt.WarehouseId,
                ToStatusCode(receipt.Status),
                receipt.Lines
                    .Select(l => new InboundReceiptLineInfo(l.Id, l.SkuId, l.ExpectedQuantity, l.ReceivedQuantity))
                    .ToList());
    }

    private static string ToStatusCode(ReceiptStatus status) => status switch
    {
        ReceiptStatus.Open => "OPEN",
        ReceiptStatus.PartiallyReceived => "PARTIALLY_RECEIVED",
        ReceiptStatus.Received => "RECEIVED",
        ReceiptStatus.PutawayInProgress => "PUTAWAY_IN_PROGRESS",
        ReceiptStatus.Completed => "COMPLETED",
        ReceiptStatus.Cancelled => "CANCELLED",
        _ => status.ToString().ToUpperInvariant(),
    };

    public async Task<InboundReceiveResult> ReceiveAsync(
        Guid requestId,
        Guid receiptId,
        Guid receiptLineId,
        int quantity,
        Guid receivingLocationId,
        string receivingStatus,
        CancellationToken cancellationToken)
    {
        var result = await receiveItems.Handle(
            new ReceiveItemsCommand(
                requestId,
                receiptId,
                receiptLineId,
                quantity,
                receivingLocationId,
                Enum.Parse<ReceivingStockStatus>(receivingStatus, ignoreCase: true)),
            cancellationToken);
        if (result.Outcome == ReceiveItemsOutcome.Received && result.Disposition != ReceivingDisposition.Matched)
        {
            WmsMetrics.ReceivingDiscrepanciesTotal.Add(1);
        }

        return new InboundReceiveResult(
            result.Outcome == ReceiveItemsOutcome.Received ? InboundReceiveOutcome.Received : InboundReceiveOutcome.AlreadyRecorded,
            result.ReceiveRecordId,
            result.Disposition.ToString(),
            result.LineReceivedQuantity);
    }
}
