using Wms.Modules.Outbound.Contracts;
using Wms.Modules.Transfers.Domain;

namespace Wms.Modules.Transfers.Application;

public enum ConfirmVarianceOutcome
{
    Confirmed = 1,
    AlreadyRecorded = 2,
}

public sealed record ConfirmVarianceCommand(
    Guid TransferId,
    Guid RequestId,
    Guid TransferLineId,
    int Quantity,
    TransferDiscrepancyReason Reason,
    string? Note = null);

public sealed record ConfirmVarianceResult(
    ConfirmVarianceOutcome Outcome,
    Guid TransferId,
    Guid TransferLineId,
    Guid? DiscrepancyId,
    int LineInTransitQuantity,
    bool TransferCompleted);

public sealed class ConfirmTransferVariance(ITransferStore store)
{
    public async Task<ConfirmVarianceResult> Handle(ConfirmVarianceCommand command, CancellationToken cancellationToken)
    {
        var existing = await store.GetDiscrepancyByRequestIdAsync(command.RequestId, cancellationToken);
        if (existing is not null)
        {
            var existingTransfer = await store.GetTransferAsync(command.TransferId, cancellationToken)
                ?? throw new TransferNotFoundException(command.TransferId);
            var existingLine = existingTransfer.GetLine(existing.TransferLineId);
            return new ConfirmVarianceResult(
                ConfirmVarianceOutcome.AlreadyRecorded,
                command.TransferId,
                existing.TransferLineId,
                existing.Id,
                existingLine.InTransitQuantity,
                existingTransfer.Status == TransferStatus.Completed);
        }

        var transfer = await store.GetTransferAsync(command.TransferId, cancellationToken)
            ?? throw new TransferNotFoundException(command.TransferId);

        if (transfer.Status is not (TransferStatus.InTransit or TransferStatus.Receiving))
        {
            throw new InvalidTransferStateException($"Transfer {transfer.Status} durumundayken variance confirm edilemez.");
        }

        var line = transfer.GetLine(command.TransferLineId);

        if (command.Quantity > line.InTransitQuantity)
        {
            throw new InvalidTransferStateException(
                $"Variance açık InTransit'i aşamaz: InTransit {line.InTransitQuantity}, attempt {command.Quantity}.");
        }

        var discrepancy = TransferDiscrepancy.Create(
            command.RequestId,
            line.Id,
            command.Quantity,
            command.Reason,
            command.Note);

        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            var fresh = await store.GetTransferAsync(command.TransferId, cancellationToken)
                ?? throw new TransferNotFoundException(command.TransferId);
            var freshLine = fresh.GetLine(command.TransferLineId);

            freshLine.ConfirmVariance(command.Quantity);

            var allClosed = fresh.Lines.All(l => l.IsClosed);
            var completed = false;
            if (allClosed)
            {
                fresh.MarkCompletedIfAllClosed();
                completed = true;
            }

            await store.AddDiscrepancyAsync(discrepancy, cancellationToken);

            var outcome = await store.SaveChangesAsync(cancellationToken);
            if (outcome == TransferSaveOutcome.DuplicateRequest)
            {
                await store.RollbackTransactionAsync(cancellationToken);
                var winner = await store.GetDiscrepancyByRequestIdAsync(command.RequestId, cancellationToken);
                if (winner is not null)
                {
                    var winnerTransfer = await store.GetTransferAsync(command.TransferId, cancellationToken);
                    var winnerLine = winnerTransfer!.GetLine(winner.TransferLineId);
                    return new ConfirmVarianceResult(
                        ConfirmVarianceOutcome.AlreadyRecorded,
                        command.TransferId,
                        winner.TransferLineId,
                        winner.Id,
                        winnerLine.InTransitQuantity,
                        winnerTransfer.Status == TransferStatus.Completed);
                }

                throw new InvalidOperationException($"Variance çakıştı ama kayıt bulunamadı: {command.RequestId}");
            }

            await store.CommitTransactionAsync(cancellationToken);

            return new ConfirmVarianceResult(
                ConfirmVarianceOutcome.Confirmed,
                fresh.Id,
                freshLine.Id,
                discrepancy.Id,
                freshLine.InTransitQuantity,
                completed);
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
