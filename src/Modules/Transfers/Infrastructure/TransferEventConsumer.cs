using Microsoft.Extensions.Logging;
using System.Text.Json;
using Wms.Modules.Transfers.Application;
using Wms.Modules.Transfers.Domain;
using Wms.Integration.Contracts;
using Wms.Integration.Inbox;
using Wms.Integration.Messaging;

namespace Wms.Modules.Transfers.Infrastructure;

public sealed class TransferEventConsumer(
    ITransferStore store,
    ShipTransfer shipTransfer,
    ILogger<TransferEventConsumer> logger) : IIntegrationConsumer
{
    public const string ConsumerName = "transfers";

    public string Name => ConsumerName;

    public string QueueName => "transfers-inbox";

    public IReadOnlyList<string> BindingRoutingKeys { get; } =
    [
        $"{IntegrationEventTypes.ShipmentShipped}.v{IntegrationEventTypes.CurrentVersion}",
        $"{IntegrationEventTypes.ReceiptCompleted}.v{IntegrationEventTypes.CurrentVersion}",
    ];

    public async Task<ConsumerProcessingResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var existing = await store.GetInboxMessageAsync(ConsumerName, envelope.EventId, cancellationToken);
        if (existing is not null)
        {
            logger.LogDebug("Duplicate event ignored ({EventType} {EventId})", envelope.EventType, envelope.EventId);
            return ConsumerProcessingResult.Ack;
        }

        switch (envelope.EventType)
        {
            case IntegrationEventTypes.ShipmentShipped:
                await HandleShipmentShippedAsync(envelope, cancellationToken);
                break;
            case IntegrationEventTypes.ReceiptCompleted:
                await HandleReceiptCompletedAsync(envelope, cancellationToken);
                break;
            default:
                logger.LogWarning("Bilinmeyen event tipi yok sayıldı: {EventType} {EventId}", envelope.EventType, envelope.EventId);
                return ConsumerProcessingResult.Ack;
        }

        await store.AddInboxMessageAsync(InboxMessage.Create(ConsumerName, envelope.EventId), cancellationToken);
        _ = await store.SaveChangesAsync(cancellationToken);

        return ConsumerProcessingResult.Ack;
    }

    private async Task HandleShipmentShippedAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var shipped = JsonSerializer.Deserialize<ShipmentShippedV1>(envelope.Payload);
        if (shipped is null)
        {
            throw new InvalidOperationException($"ShipmentShipped payload çözümlenemedi: {envelope.EventId}");
        }

        var transfer = await store.GetTransferByOutboundOrderIdAsync(shipped.OrderId, cancellationToken);
        if (transfer is null)
        {
            logger.LogDebug("ShipmentShipped eventi bir transfer'e ait değil (order {OrderId}) — ignore", shipped.OrderId);
            return;
        }

        if (transfer.Status != TransferStatus.Allocated)
        {
            logger.LogDebug("Transfer {TransferId} zaten {Status} — ShipmentShipped yok sayılır", transfer.Id, transfer.Status);
            return;
        }

        // Idempotent orchestration: ShipTransfer kendi deterministik RequestId'leriyle
        // retry-safe'dir; event yalnız tetikleyicidir.
        await shipTransfer.Handle(new ShipTransferCommand(transfer.Id), cancellationToken);
    }

    private async Task HandleReceiptCompletedAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var completed = JsonSerializer.Deserialize<ReceiptCompletedV1>(envelope.Payload);
        if (completed is null)
        {
            throw new InvalidOperationException($"ReceiptCompleted payload çözümlenemedi: {envelope.EventId}");
        }

        var transfer = await store.GetTransferByInboundReceiptIdAsync(completed.ReceiptId, cancellationToken);
        if (transfer is null)
        {
            logger.LogDebug("ReceiptCompleted eventi bir transfer'e ait değil (receipt {ReceiptId}) — ignore", completed.ReceiptId);
            return;
        }

        if (transfer.Status is not (TransferStatus.InTransit or TransferStatus.Receiving))
        {
            logger.LogDebug("Transfer {TransferId} zaten {Status} — ReceiptCompleted yok sayılır", transfer.Id, transfer.Status);
            return;
        }

        // Yalnızca tüm line'lar kapandıysa complete — dangling InTransit yasak (domain guard + idempotent).
        if (transfer.Lines.All(l => l.IsClosed))
        {
            transfer.MarkCompletedIfAllClosed();
            await store.SaveChangesAsync(cancellationToken);
        }
    }
}
