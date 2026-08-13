using Wms.Modules.Facility.Contracts;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.MasterData.Contracts;

namespace Wms.Modules.Inventory.Application;

public sealed record ChangeStatusCommand(
    Guid RequestId,
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    InventoryStatus FromStatus,
    InventoryStatus ToStatus,
    int Quantity);

public sealed class ChangeInventoryStatus(
    IInventoryStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility)
{
    public async Task<MovementResult> Handle(ChangeStatusCommand command, CancellationToken cancellationToken)
    {
        var existing = await store.GetMovementByRequestIdAsync(command.RequestId, cancellationToken);
        if (existing is not null)
        {
            return new MovementResult(MovementOutcome.AlreadyRecorded, existing.Id);
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

        var location = await facility.GetLocationAsync(command.LocationId, cancellationToken)
            ?? throw new LocationValidationException($"Location bulunamadı: {command.LocationId}");
        if (!location.IsActive || location.WarehouseId != command.WarehouseId)
        {
            throw new LocationValidationException($"Location {location.Code} aktif değil veya verilen warehouse'a ait değil.");
        }

        if (!location.HoldsInventory)
        {
            throw new LocationValidationException($"Location {location.Code} stok tutabilir değil (HoldsInventory=false).");
        }

        var movement = InventoryMovement.CreateStatusChange(
            command.RequestId,
            command.SkuId,
            command.WarehouseId,
            command.LocationId,
            command.FromStatus,
            command.ToStatus,
            command.Quantity);

        var sourceBalance = await store.GetBalanceAsync(
                command.WarehouseId,
                command.SkuId,
                command.LocationId,
                command.FromStatus,
                cancellationToken)
            ?? throw new InventoryBalanceNotFoundException(Guid.Empty);

        if (sourceBalance.UnallocatedQuantity < command.Quantity)
        {
            throw new InsufficientInventoryException(
                command.WarehouseId,
                command.SkuId,
                command.Quantity,
                sourceBalance.UnallocatedQuantity);
        }

        var destinationBalance = await store.GetBalanceAsync(
            command.WarehouseId,
            command.SkuId,
            command.LocationId,
            command.ToStatus,
            cancellationToken);

        var ledgerEntries = new[]
        {
            InventoryLedgerEntry.Create(
                command.RequestId,
                command.SkuId,
                command.WarehouseId,
                command.LocationId,
                command.FromStatus,
                LedgerEntryType.StatusChangedFrom,
                -command.Quantity,
                0,
                movement.Id),
            InventoryLedgerEntry.Create(
                command.RequestId,
                command.SkuId,
                command.WarehouseId,
                command.LocationId,
                command.ToStatus,
                LedgerEntryType.StatusChangedTo,
                command.Quantity,
                0,
                movement.Id),
        };

        var outcome = await store.ExecuteMovementAsync(
            movement,
            ledgerEntries,
            sourceBalance.Id,
            destinationBalance?.Id,
            command.Quantity,
            evidence: null,
            cancellationToken);

        if (outcome == StoreSaveOutcome.DuplicateRequest)
        {
            var duplicate = await store.GetMovementByRequestIdAsync(command.RequestId, cancellationToken);
            if (duplicate is not null)
            {
                return new MovementResult(MovementOutcome.AlreadyRecorded, duplicate.Id);
            }

            throw new InvalidOperationException($"RequestId daha önce kullanılmış ama movement bulunamadı: {command.RequestId}");
        }

        return new MovementResult(MovementOutcome.Performed, movement.Id);
    }
}
