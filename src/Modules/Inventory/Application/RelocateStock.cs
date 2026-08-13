using Wms.Modules.Facility.Contracts;
using Wms.Modules.Inventory.Application.Accuracy.Scanning;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Domain.Accuracy.Scanning;
using Wms.Modules.MasterData.Contracts;

namespace Wms.Modules.Inventory.Application;

public enum MovementOutcome
{
    Performed = 1,
    AlreadyRecorded = 2,
}

public sealed record MovementResult(MovementOutcome Outcome, Guid MovementId);

public sealed record RelocateCommand(
    Guid RequestId,
    Guid SkuId,
    Guid WarehouseId,
    Guid SourceLocationId,
    Guid DestinationLocationId,
    int Quantity);

public sealed class RelocateStock(
    IInventoryStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility)
{
    public async Task<MovementResult> Handle(RelocateCommand command, CancellationToken cancellationToken) =>
        await Handle(command, seed: null, cancellationToken);

    public async Task<MovementResult> Handle(RelocateCommand command, ScanEvidenceSeed? seed, CancellationToken cancellationToken)
    {
        var existing = await store.GetMovementByRequestIdAsync(command.RequestId, cancellationToken);
        if (existing is not null)
        {
            return new MovementResult(MovementOutcome.AlreadyRecorded, existing.Id);
        }

        await ValidateReferencesAsync(command, cancellationToken);

        var movement = InventoryMovement.CreateRelocate(
            command.RequestId,
            command.SkuId,
            command.WarehouseId,
            command.SourceLocationId,
            command.DestinationLocationId,
            command.Quantity);

        var sourceBalance = await store.GetBalanceAsync(
                command.WarehouseId,
                command.SkuId,
                command.SourceLocationId,
                InventoryStatus.Available,
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
            command.DestinationLocationId,
            InventoryStatus.Available,
            cancellationToken);

        var ledgerEntries = new[]
        {
            InventoryLedgerEntry.Create(
                command.RequestId,
                command.SkuId,
                command.WarehouseId,
                command.SourceLocationId,
                InventoryStatus.Available,
                LedgerEntryType.RelocatedOut,
                -command.Quantity,
                0,
                movement.Id),
            InventoryLedgerEntry.Create(
                command.RequestId,
                command.SkuId,
                command.WarehouseId,
                command.DestinationLocationId,
                InventoryStatus.Available,
                LedgerEntryType.RelocatedIn,
                command.Quantity,
                0,
                movement.Id),
        };

        var evidence = seed is null
            ? null
            : ScanMovementEvidence.Create(
                movement.Id,
                command.RequestId,
                command.WarehouseId,
                command.SkuId,
                command.SourceLocationId,
                command.DestinationLocationId,
                seed.SourceScanValue,
                seed.SkuScanValue,
                seed.DestinationScanValue,
                command.Quantity,
                seed.DeviceId,
                seed.OperatorId);

        var outcome = await store.ExecuteMovementAsync(
            movement,
            ledgerEntries,
            sourceBalance.Id,
            destinationBalance?.Id,
            command.Quantity,
            evidence,
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

    private async Task ValidateReferencesAsync(RelocateCommand command, CancellationToken cancellationToken)
    {
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

        var source = await facility.GetLocationAsync(command.SourceLocationId, cancellationToken)
            ?? throw new LocationValidationException($"Source location bulunamadı: {command.SourceLocationId}");
        ValidateLocation(command.WarehouseId, source, "Source");

        var destination = await facility.GetLocationAsync(command.DestinationLocationId, cancellationToken)
            ?? throw new LocationValidationException($"Destination location bulunamadı: {command.DestinationLocationId}");
        ValidateLocation(command.WarehouseId, destination, "Destination");
    }

    private static void ValidateLocation(Guid warehouseId, LocationInfo location, string role)
    {
        if (!location.IsActive)
        {
            throw new LocationValidationException($"{role} location aktif değil: {location.Code}");
        }

        if (location.WarehouseId != warehouseId)
        {
            throw new LocationValidationException($"{role} location {location.Code} verilen warehouse'a ait değil — cross-warehouse relocation yasaktır (Transfer domain'ine aittir).");
        }

        if (!location.HoldsInventory)
        {
            throw new LocationValidationException($"{role} location {location.Code} stok tutabilir değil (HoldsInventory=false).");
        }
    }
}
