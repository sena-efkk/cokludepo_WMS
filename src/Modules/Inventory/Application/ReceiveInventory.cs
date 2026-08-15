using Wms.Modules.Facility.Contracts;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.MasterData.Contracts;

namespace Wms.Modules.Inventory.Application;

public enum ReceiveInventoryOutcome
{
    Recorded = 1,
    DuplicateRequest = 2,
}

public sealed record ReceiveInventoryCommand(
    Guid RequestId,
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    InventoryStatus Status,
    int Quantity,
    string? ReferenceType,
    Guid? ReferenceId);

public sealed record ReceiveInventoryResult(ReceiveInventoryOutcome Outcome, Guid RequestId);

public sealed class ReceiveInventory(
    IInventoryStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility)
{
    public async Task<ReceiveInventoryResult> Handle(ReceiveInventoryCommand command, CancellationToken cancellationToken)
    {
        var alreadyRecorded = await store.OperationExistsAsync(command.RequestId, cancellationToken);
        if (alreadyRecorded)
        {
            return new ReceiveInventoryResult(ReceiveInventoryOutcome.DuplicateRequest, command.RequestId);
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
            throw new LocationValidationException($"Location {location.Code} stok tutamaz (HoldsInventory=false).");
        }

        var outcome = await store.ExecuteReceiveAsync(
            command.RequestId,
            command.SkuId,
            command.WarehouseId,
            command.LocationId,
            command.Status,
            command.Quantity,
            command.ReferenceType,
            command.ReferenceId,
            cancellationToken);

        return new ReceiveInventoryResult(
            outcome == StoreSaveOutcome.Saved ? ReceiveInventoryOutcome.Recorded : ReceiveInventoryOutcome.DuplicateRequest,
            command.RequestId);
    }
}
