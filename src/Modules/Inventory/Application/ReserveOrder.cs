using System.Security.Cryptography;
using System.Text;
using Wms.Modules.Facility.Contracts;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.MasterData.Contracts;

namespace Wms.Modules.Inventory.Application;

public enum ReserveOrderOutcome
{
    Reserved = 1,
    InsufficientStock = 2,
    AlreadyRecorded = 3,
}

public sealed record ReserveOrderLineInput(Guid SkuId, int Quantity);

public sealed record ReserveOrderCommand(
    Guid RequestId,
    Guid WarehouseId,
    IReadOnlyList<ReserveOrderLineInput> Lines,
    string Purpose);

public sealed record ReserveOrderResult(
    ReserveOrderOutcome Outcome,
    IReadOnlyList<InventoryReservation> Reservations);

public sealed class ReserveOrder(
    IInventoryStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility)
{
    public async Task<ReserveOrderResult> Handle(ReserveOrderCommand command, CancellationToken cancellationToken)
    {
        if (command.Lines.Count == 0)
        {
            throw new ArgumentException("ReserveOrder en az bir line içermelidir.");
        }

        var lineRequestIds = command.Lines
            .DistinctBy(l => l.SkuId)
            .ToDictionary(l => l.SkuId, l => DeriveLineRequestId(command.RequestId, l.SkuId));

        var existing = await RebuildExistingAsync(lineRequestIds, cancellationToken);
        if (existing is not null && existing.Count == lineRequestIds.Count)
        {
            return new ReserveOrderResult(ReserveOrderOutcome.AlreadyRecorded, existing);
        }

        foreach (var line in command.Lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException("ReserveOrder line quantity pozitif olmalıdır.");
            }

            var sku = await masterData.GetSkuAsync(line.SkuId, cancellationToken)
                ?? throw new SkuValidationException($"SKU bulunamadı: {line.SkuId}");
            if (!sku.IsActive)
            {
                throw new SkuValidationException($"SKU aktif değil: {sku.Code}");
            }
        }

        var warehouse = await facility.GetWarehouseAsync(command.WarehouseId, cancellationToken)
            ?? throw new WarehouseValidationException($"Warehouse bulunamadı: {command.WarehouseId}");
        if (!warehouse.IsActive)
        {
            throw new WarehouseValidationException($"Warehouse aktif değil: {warehouse.Code}");
        }

        await store.AddOperationAsync(command.RequestId, "ReserveOrder", cancellationToken);

        try
        {
            await store.BeginTransactionAsync(cancellationToken);

            var reservations = new List<InventoryReservation>();
            var ledgerEntries = new List<InventoryLedgerEntry>();

            // Deadlock güvenli sıra: SKU id'ye göre artan kilitleme.
            var orderedLines = command.Lines
                .GroupBy(l => l.SkuId)
                .Select(g => new ReserveOrderLineInput(g.Key, g.Sum(l => l.Quantity)))
                .OrderBy(l => l.SkuId)
                .ToList();

            foreach (var line in orderedLines)
            {
                var lockedBalances = await store.LockAvailableBalancesAsync(
                    command.WarehouseId,
                    line.SkuId,
                    cancellationToken);
                var totalAvailable = lockedBalances.Sum(b => b.Available);
                if (totalAvailable < line.Quantity)
                {
                    throw new InsufficientInventoryException(
                        command.WarehouseId,
                        line.SkuId,
                        line.Quantity,
                        totalAvailable);
                }

                var reservation = InventoryReservation.Create(
                    lineRequestIds[line.SkuId],
                    line.SkuId,
                    command.WarehouseId,
                    line.Quantity);

                var remaining = line.Quantity;
                foreach (var balance in lockedBalances)
                {
                    if (remaining == 0)
                    {
                        break;
                    }

                    if (balance.Available <= 0)
                    {
                        continue;
                    }

                    var take = Math.Min(remaining, balance.Available);
                    balance.AddAllocated(take);
                    reservation.AddLine(balance.LocationId, take);
                    ledgerEntries.Add(InventoryLedgerEntry.Create(
                        reservation.RequestId,
                        line.SkuId,
                        command.WarehouseId,
                        balance.LocationId,
                        InventoryStatus.Available,
                        LedgerEntryType.Reserved,
                        0,
                        take));
                    remaining -= take;
                }

                if (remaining > 0)
                {
                    throw new InsufficientInventoryException(
                        command.WarehouseId,
                        line.SkuId,
                        line.Quantity,
                        totalAvailable);
                }

                reservations.Add(reservation);
            }

            await store.AddReservationAsync(reservations[0], cancellationToken);
            for (var i = 1; i < reservations.Count; i++)
            {
                await store.AddReservationAsync(reservations[i], cancellationToken);
            }

            await store.AddLedgerEntriesAsync(ledgerEntries, cancellationToken);

            var outcome = await store.SaveChangesAsync(cancellationToken);
            if (outcome == StoreSaveOutcome.DuplicateRequest)
            {
                await store.RollbackTransactionAsync(cancellationToken);
                var duplicates = await RebuildExistingAsync(lineRequestIds, cancellationToken);
                if (duplicates is not null && duplicates.Count == lineRequestIds.Count)
                {
                    return new ReserveOrderResult(ReserveOrderOutcome.AlreadyRecorded, duplicates);
                }

                throw new InvalidOperationException($"RequestId çakıştı ama reservation'lar bulunamadı: {command.RequestId}");
            }

            await store.CommitTransactionAsync(cancellationToken);
            return new ReserveOrderResult(ReserveOrderOutcome.Reserved, reservations);
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private async Task<IReadOnlyList<InventoryReservation>?> RebuildExistingAsync(
        IReadOnlyDictionary<Guid, Guid> lineRequestIds,
        CancellationToken cancellationToken)
    {
        var result = new List<InventoryReservation>();
        foreach (var derivedId in lineRequestIds.Values)
        {
            var reservation = await store.GetReservationByRequestIdAsync(derivedId, cancellationToken);
            if (reservation is null)
            {
                return null;
            }

            result.Add(reservation);
        }

        return result;
    }

    internal static Guid DeriveLineRequestId(Guid orderRequestId, Guid skuId)
    {
        var bytes = Encoding.UTF8.GetBytes($"{orderRequestId:N}:{skuId:N}");
        var hash = MD5.HashData(bytes);
        return new Guid(hash);
    }
}
