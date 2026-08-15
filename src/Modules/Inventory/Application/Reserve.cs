using Wms.Modules.Facility.Contracts;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.MasterData.Contracts;

namespace Wms.Modules.Inventory.Application;

public sealed record ReserveCommand(
    Guid RequestId,
    Guid SkuId,
    Guid WarehouseId,
    int RequestedQuantity,
    string Purpose);

public sealed class Reserve(
    IInventoryStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility)
{
    public async Task<InventoryReservation> Handle(ReserveCommand command, CancellationToken cancellationToken)
    {
        var existing = await store.GetReservationByRequestIdAsync(command.RequestId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var sku = await masterData.GetSkuAsync(command.SkuId, cancellationToken)
            ?? throw new SkuValidationException($"SKU bulunamadı: {command.SkuId}");
        if (!sku.IsActive)
        {
            throw new SkuValidationException($"SKU aktif değil: {sku.Code}");
        }

        var warehouse = await facility.GetWarehouseAsync(command.WarehouseId, cancellationToken)
            ?? throw new WarehouseValidationException($"Warehouse bulunamadı: {command.WarehouseId}");
        if (!warehouse.IsActive)
        {
            throw new WarehouseValidationException($"Warehouse aktif değil: {warehouse.Code}");
        }

        await store.AddOperationAsync(command.RequestId, "Reserve", cancellationToken);

        try
        {
            await store.BeginTransactionAsync(cancellationToken);

            var lockedBalances = await store.LockAvailableBalancesAsync(command.WarehouseId, command.SkuId, cancellationToken);
            var totalAvailable = lockedBalances.Sum(b => b.Available);
            if (totalAvailable < command.RequestedQuantity)
            {
                throw new InsufficientInventoryException(command.WarehouseId, command.SkuId, command.RequestedQuantity, totalAvailable);
            }

            var reservation = InventoryReservation.Create(
                command.RequestId,
                command.SkuId,
                command.WarehouseId,
                command.RequestedQuantity);

            var ledgerEntries = new List<InventoryLedgerEntry>();
            var remaining = command.RequestedQuantity;
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
                    command.RequestId,
                    command.SkuId,
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
                throw new InsufficientInventoryException(command.WarehouseId, command.SkuId, command.RequestedQuantity, totalAvailable);
            }

            await store.AddReservationAsync(reservation, cancellationToken);
            await store.AddLedgerEntriesAsync(ledgerEntries, cancellationToken);

            var outcome = await store.SaveChangesAsync(cancellationToken);
            if (outcome == StoreSaveOutcome.DuplicateRequest)
            {
                await store.RollbackTransactionAsync(cancellationToken);
                var duplicate = await store.GetReservationByRequestIdAsync(command.RequestId, cancellationToken);
                if (duplicate is not null)
                {
                    return duplicate;
                }

                throw new InvalidOperationException($"RequestId daha önce kullanılmış ama reservation bulunamadı: {command.RequestId}");
            }

            await store.CommitTransactionAsync(cancellationToken);
            return reservation;
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
