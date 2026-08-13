using Wms.Modules.Facility.Contracts;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Domain.Accuracy;
using Wms.Modules.MasterData.Contracts;

namespace Wms.Modules.Inventory.Application.Accuracy;

public enum SignalOutcome
{
    Recorded = 1,
    AlreadyRecorded = 2,
}

public sealed record ReportSignalResult(SignalOutcome Outcome, Guid SignalId);

public sealed record ReportPickNotFoundCommand(
    Guid RequestId,
    Guid SkuId,
    Guid WarehouseId,
    Guid LocationId,
    AccuracySourceType SourceType,
    Guid? SourceReferenceId,
    DateTime? OccurredAt);

public sealed class ReportPickNotFound(
    IInventoryStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility)
{
    public async Task<ReportSignalResult> Handle(ReportPickNotFoundCommand command, CancellationToken cancellationToken)
    {
        var existing = await store.GetAccuracySignalByRequestIdAsync(command.RequestId, cancellationToken);
        if (existing is not null)
        {
            return new ReportSignalResult(SignalOutcome.AlreadyRecorded, existing.Id);
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

        var balance = await store.GetBalanceAsync(
            command.WarehouseId,
            command.SkuId,
            command.LocationId,
            InventoryStatus.Available,
            cancellationToken);

        var signal = InventoryAccuracySignal.CreatePickNotFound(
            command.RequestId,
            command.SourceType,
            command.SkuId,
            command.WarehouseId,
            command.LocationId,
            command.SourceReferenceId,
            command.OccurredAt ?? DateTime.UtcNow,
            balance?.Quantity ?? 0,
            balance?.Allocated ?? 0,
            balance?.Available ?? 0,
            InventoryStatus.Available);

        await store.AddAccuracySignalAsync(signal, cancellationToken);
        var outcome = await store.SaveChangesAsync(cancellationToken);

        if (outcome == StoreSaveOutcome.DuplicateRequest)
        {
            var duplicate = await store.GetAccuracySignalByRequestIdAsync(command.RequestId, cancellationToken);
            if (duplicate is not null)
            {
                return new ReportSignalResult(SignalOutcome.AlreadyRecorded, duplicate.Id);
            }

            throw new InvalidOperationException($"RequestId daha önce kullanılmış ama signal bulunamadı: {command.RequestId}");
        }

        return new ReportSignalResult(SignalOutcome.Recorded, signal.Id);
    }
}
