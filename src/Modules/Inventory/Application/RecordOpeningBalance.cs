using Wms.Modules.Facility.Contracts;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.MasterData.Contracts;

namespace Wms.Modules.Inventory.Application;

public sealed record RecordOpeningBalanceCommand(
    Guid RequestId,
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    InventoryStatus Status,
    int Quantity);

public enum OpeningBalanceOutcome
{
    Recorded = 1,
    AlreadyRecorded = 2,
}

public sealed record OpeningBalanceResult(OpeningBalanceOutcome Outcome, Guid RequestId);

public sealed class RecordOpeningBalance(
    IInventoryStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility)
{
    public async Task<OpeningBalanceResult> Handle(RecordOpeningBalanceCommand command, CancellationToken cancellationToken)
    {
        if (await store.OperationExistsAsync(command.RequestId, cancellationToken))
        {
            return new OpeningBalanceResult(OpeningBalanceOutcome.AlreadyRecorded, command.RequestId);
        }

        await ValidateReferencesAsync(command, cancellationToken);

        var recorded = await store.TryRecordOpeningBalanceAtomicAsync(
            command.RequestId,
            command.SkuId,
            command.WarehouseId,
            command.LocationId,
            command.Status,
            command.Quantity,
            cancellationToken);

        return recorded
            ? new OpeningBalanceResult(OpeningBalanceOutcome.Recorded, command.RequestId)
            : new OpeningBalanceResult(OpeningBalanceOutcome.AlreadyRecorded, command.RequestId);
    }

    private async Task ValidateReferencesAsync(RecordOpeningBalanceCommand command, CancellationToken cancellationToken)
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

        var location = await facility.GetLocationAsync(command.LocationId, cancellationToken)
            ?? throw new LocationValidationException($"Location bulunamadı: {command.LocationId}");
        if (!location.IsActive)
        {
            throw new LocationValidationException($"Location aktif değil: {location.Code}");
        }

        if (location.WarehouseId != command.WarehouseId)
        {
            throw new LocationValidationException($"Location {location.Code} verilen warehouse'a ait değil.");
        }

        if (!location.HoldsInventory)
        {
            throw new LocationValidationException($"Location {location.Code} stok tutabilir değil (HoldsInventory=false).");
        }
    }
}
