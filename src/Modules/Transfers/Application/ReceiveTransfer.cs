using Wms.Modules.Inbound.Contracts;
using Wms.Modules.Transfers.Domain;

namespace Wms.Modules.Transfers.Application;

public enum ReceiveTransferOutcome
{
    Received = 1,
    AlreadyRecorded = 2,
}

public sealed record ReceiveTransferCommand(
    Guid TransferId,
    Guid RequestId,
    Guid TransferLineId,
    int Quantity,
    Guid ReceivingLocationId,
    string ReceivingStatus);

public sealed record ReceiveTransferResult(
    ReceiveTransferOutcome Outcome,
    Guid TransferId,
    Guid TransferLineId,
    int LineReceivedQuantity,
    int LineInTransitQuantity);

public sealed class ReceiveTransfer(
    ITransferStore store,
    IInboundContract inbound)
{
    public async Task<ReceiveTransferResult> Handle(ReceiveTransferCommand command, CancellationToken cancellationToken)
    {
        var transfer = await store.GetTransferAsync(command.TransferId, cancellationToken)
            ?? throw new TransferNotFoundException(command.TransferId);

        if (transfer.Status is not (TransferStatus.InTransit or TransferStatus.Receiving))
        {
            throw new InvalidTransferStateException($"Transfer {transfer.Status} durumundayken receive yapılamaz.");
        }

        if (transfer.InboundReceiptId is null)
        {
            throw new InvalidTransferStateException("Destination receipt oluşturulmamış transfer receive edilemez.");
        }

        var line = transfer.GetLine(command.TransferLineId);

        var existingRecord = await store.GetReceiveRecordByRequestIdAsync(command.RequestId, cancellationToken);
        if (existingRecord is not null)
        {
            return new ReceiveTransferResult(
                ReceiveTransferOutcome.AlreadyRecorded,
                transfer.Id,
                line.Id,
                line.ReceivedQuantity,
                line.InTransitQuantity);
        }

        if (command.Quantity <= 0)
        {
            throw new ArgumentException("Receive quantity pozitif olmalıdır.", nameof(command.Quantity));
        }

        if (line.ReceivedQuantity + command.Quantity > line.ShippedQuantity)
        {
            throw new OverReceiptRejectedException(line.ShippedQuantity, line.ReceivedQuantity, command.Quantity);
        }

        if (line.InboundReceiptLineId is null)
        {
            throw new InvalidTransferStateException("Transfer line destination receipt ile eşleşmemiş.");
        }

        // Destination stok girişi Inbound path üzerinden — idempotent (AlreadyRecorded retry güvenli).
        var receiveResult = await inbound.ReceiveAsync(
            command.RequestId,
            transfer.InboundReceiptId.Value,
            line.InboundReceiptLineId.Value,
            command.Quantity,
            command.ReceivingLocationId,
            command.ReceivingStatus,
            cancellationToken);

        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            var fresh = await store.GetTransferAsync(command.TransferId, cancellationToken)
                ?? throw new TransferNotFoundException(command.TransferId);
            var freshLine = fresh.GetLine(command.TransferLineId);

            var duplicate = await store.GetReceiveRecordByRequestIdAsync(command.RequestId, cancellationToken);
            if (duplicate is not null)
            {
                await store.CommitTransactionAsync(cancellationToken);
                return new ReceiveTransferResult(
                    ReceiveTransferOutcome.AlreadyRecorded,
                    fresh.Id,
                    freshLine.Id,
                    freshLine.ReceivedQuantity,
                    freshLine.InTransitQuantity);
            }

            var record = TransferReceiveRecord.Create(
                command.RequestId,
                freshLine.Id,
                command.Quantity,
                command.ReceivingLocationId,
                command.ReceivingStatus);

            freshLine.Receive(command.Quantity);
            fresh.MarkReceiving();

            var allClosed = fresh.Lines.All(l => l.IsClosed);
            if (allClosed)
            {
                fresh.MarkCompletedIfAllClosed();
            }

            await store.AddReceiveRecordAsync(record, cancellationToken);

            var outcome = await store.SaveChangesAsync(cancellationToken);
            if (outcome == TransferSaveOutcome.DuplicateRequest)
            {
                await store.RollbackTransactionAsync(cancellationToken);
                var winner = await store.GetReceiveRecordByRequestIdAsync(command.RequestId, cancellationToken);
                if (winner is not null)
                {
                    var winnerTransfer = await store.GetTransferAsync(command.TransferId, cancellationToken);
                    var winnerLine = winnerTransfer!.GetLine(command.TransferLineId);
                    return new ReceiveTransferResult(
                        ReceiveTransferOutcome.AlreadyRecorded,
                        command.TransferId,
                        winnerLine.Id,
                        winnerLine.ReceivedQuantity,
                        winnerLine.InTransitQuantity);
                }

                throw new InvalidOperationException($"Receive çakıştı ama kayıt bulunamadı: {command.RequestId}");
            }

            await store.CommitTransactionAsync(cancellationToken);

            return new ReceiveTransferResult(
                ReceiveTransferOutcome.Received,
                fresh.Id,
                freshLine.Id,
                freshLine.ReceivedQuantity,
                freshLine.InTransitQuantity);
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
