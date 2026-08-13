using Wms.Modules.Facility.Contracts;
using Wms.Modules.Inventory.Domain.Accuracy.Scanning;
using Wms.Modules.MasterData.Contracts;

namespace Wms.Modules.Inventory.Application.Accuracy.Scanning;

public enum ScannedRelocationStatus
{
    Completed = 1,
    Rejected = 2,
    DuplicateRequest = 3,
}

public enum ScanRejectionCode
{
    ScanRequired = 1,
    SourceNotFound = 2,
    SkuNotFound = 3,
    DestinationNotFound = 4,
    LocationInactive = 5,
    WrongWarehouse = 6,
    SkuNotAtSource = 7,
    InsufficientAvailableStock = 8,
    DestinationNotAllowed = 9,
}

public sealed record ScannedRelocationCommand(
    Guid RequestId,
    Guid WarehouseId,
    string SourceLocationScan,
    string SkuScan,
    string DestinationLocationScan,
    int Quantity,
    string? DeviceId = null,
    string? OperatorId = null);

public sealed record ScanEvidenceSeed(
    string SourceScanValue,
    string SkuScanValue,
    string DestinationScanValue,
    string? DeviceId,
    string? OperatorId);

public sealed record ScannedRelocationResult(
    ScannedRelocationStatus Status,
    ScanRejectionCode? RejectionCode,
    string? RejectionReason,
    Guid? MovementId,
    Guid? EvidenceId,
    Guid? SkuId,
    Guid? SourceLocationId,
    Guid? DestinationLocationId,
    int? Quantity);

public sealed class ExecuteScannedRelocation(
    IInventoryStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility,
    RelocateStock relocateStock)
{
    private static readonly HashSet<string> PutawayBlockedLocationTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Dock", "Shipping", "Staging", "Packing", "Receiving", "CrossDock",
        };

    public async Task<ScannedRelocationResult> Handle(ScannedRelocationCommand command, CancellationToken cancellationToken)
    {
        var existing = await store.GetMovementByRequestIdAsync(command.RequestId, cancellationToken);
        if (existing is not null)
        {
            var existingEvidence = await store.GetScanEvidenceByMovementIdAsync(existing.Id, cancellationToken);
            return new ScannedRelocationResult(
                ScannedRelocationStatus.DuplicateRequest,
                null,
                null,
                existing.Id,
                existingEvidence?.Id,
                existing.SkuId,
                existing.SourceLocationId,
                existing.DestinationLocationId,
                existing.Quantity);
        }

        if (string.IsNullOrWhiteSpace(command.SourceLocationScan))
        {
            return Rejected(ScanRejectionCode.ScanRequired, "Source location scan zorunludur (strict mode).");
        }

        if (string.IsNullOrWhiteSpace(command.SkuScan))
        {
            return Rejected(ScanRejectionCode.ScanRequired, "SKU barcode scan zorunludur (strict mode).");
        }

        if (string.IsNullOrWhiteSpace(command.DestinationLocationScan))
        {
            return Rejected(ScanRejectionCode.ScanRequired, "Destination location scan zorunludur (strict mode).");
        }

        var warehouse = await facility.GetWarehouseAsync(command.WarehouseId, cancellationToken);
        if (warehouse is null)
        {
            return Rejected(ScanRejectionCode.WrongWarehouse, $"Warehouse bulunamadı: {command.WarehouseId}");
        }

        if (!warehouse.IsActive)
        {
            return Rejected(ScanRejectionCode.WrongWarehouse, $"Warehouse aktif değil: {warehouse.Code}");
        }

        var source = await facility.GetLocationByCodeAsync(command.WarehouseId, command.SourceLocationScan.Trim(), cancellationToken);
        if (source is null)
        {
            var globalSource = await facility.GetLocationByCodeGlobalAsync(command.SourceLocationScan.Trim(), cancellationToken);
            if (globalSource is not null)
            {
                return Rejected(ScanRejectionCode.WrongWarehouse, $"Source location {globalSource.Code} başka bir warehouse'a ait — cross-warehouse relocation yasaktır.");
            }

            return Rejected(ScanRejectionCode.SourceNotFound, $"Source location bulunamadı: '{command.SourceLocationScan.Trim()}'");
        }

        if (!source.IsActive)
        {
            return Rejected(ScanRejectionCode.LocationInactive, $"Source location aktif değil: {source.Code}");
        }

        if (source.WarehouseId != command.WarehouseId)
        {
            return Rejected(ScanRejectionCode.WrongWarehouse, $"Source location {source.Code} bu warehouse'a ait değil.");
        }

        var sku = await masterData.GetSkuByBarcodeAsync(command.SkuScan.Trim(), cancellationToken);
        if (sku is null)
        {
            return Rejected(ScanRejectionCode.SkuNotFound, $"Barcode çözülemedi: '{command.SkuScan.Trim()}'");
        }

        if (!sku.IsActive)
        {
            return Rejected(ScanRejectionCode.SkuNotFound, $"SKU aktif değil: {sku.Code}");
        }

        var destination = await facility.GetLocationByCodeAsync(command.WarehouseId, command.DestinationLocationScan.Trim(), cancellationToken);
        if (destination is null)
        {
            var globalDestination = await facility.GetLocationByCodeGlobalAsync(command.DestinationLocationScan.Trim(), cancellationToken);
            if (globalDestination is not null)
            {
                return Rejected(ScanRejectionCode.WrongWarehouse, $"Destination location {globalDestination.Code} başka bir warehouse'a ait — cross-warehouse relocation yasaktır.");
            }

            return Rejected(ScanRejectionCode.DestinationNotFound, $"Destination location bulunamadı: '{command.DestinationLocationScan.Trim()}'");
        }

        if (!destination.IsActive)
        {
            return Rejected(ScanRejectionCode.LocationInactive, $"Destination location aktif değil: {destination.Code}");
        }

        if (destination.WarehouseId != command.WarehouseId)
        {
            return Rejected(ScanRejectionCode.WrongWarehouse, $"Destination location {destination.Code} bu warehouse'a ait değil — cross-warehouse relocation yasaktır.");
        }

        if (!destination.HoldsInventory)
        {
            return Rejected(ScanRejectionCode.DestinationNotAllowed, $"Destination {destination.Code} stok tutamaz (HoldsInventory=false).");
        }

        if (PutawayBlockedLocationTypes.Contains(destination.LocationType))
        {
            return Rejected(
                ScanRejectionCode.DestinationNotAllowed,
                $"Destination {destination.Code} ({destination.LocationType}) putaway relocation kabul etmez.");
        }

        var sourceBalance = await store.GetBalanceAsync(
            command.WarehouseId,
            sku.Id,
            source.Id,
            Domain.InventoryStatus.Available,
            cancellationToken);

        if (sourceBalance is null || sourceBalance.Quantity <= 0)
        {
            return Rejected(ScanRejectionCode.SkuNotAtSource, $"SKU {sku.Code} kaynak lokasyonda yok: {source.Code}");
        }

        if (sourceBalance.UnallocatedQuantity < command.Quantity)
        {
            return Rejected(
                ScanRejectionCode.InsufficientAvailableStock,
                $"Kaynak lokasyonda {command.Quantity} kullanılabilir stok yok: {sourceBalance.UnallocatedQuantity} (allocated {sourceBalance.Allocated}).");
        }

        var seed = new ScanEvidenceSeed(
            command.SourceLocationScan.Trim(),
            command.SkuScan.Trim(),
            command.DestinationLocationScan.Trim(),
            command.DeviceId,
            command.OperatorId);

        var relocateCommand = new RelocateCommand(
            command.RequestId,
            sku.Id,
            command.WarehouseId,
            source.Id,
            destination.Id,
            command.Quantity);

        MovementResult movementResult;
        try
        {
            movementResult = await relocateStock.Handle(relocateCommand, seed, cancellationToken);
        }
        catch (InsufficientInventoryException exception)
        {
            return Rejected(ScanRejectionCode.InsufficientAvailableStock, exception.Message);
        }
        catch (InventoryBalanceNotFoundException exception)
        {
            return Rejected(ScanRejectionCode.SkuNotAtSource, exception.Message);
        }
        catch (SkuValidationException exception)
        {
            return Rejected(ScanRejectionCode.SkuNotFound, exception.Message);
        }
        catch (WarehouseValidationException exception)
        {
            return Rejected(ScanRejectionCode.WrongWarehouse, exception.Message);
        }
        catch (LocationValidationException exception)
        {
            return Rejected(ScanRejectionCode.DestinationNotAllowed, exception.Message);
        }

        if (movementResult.Outcome == MovementOutcome.AlreadyRecorded)
        {
            return new ScannedRelocationResult(
                ScannedRelocationStatus.DuplicateRequest,
                null,
                null,
                movementResult.MovementId,
                null,
                sku.Id,
                source.Id,
                destination.Id,
                command.Quantity);
        }

        var evidence = await store.GetScanEvidenceByMovementIdAsync(movementResult.MovementId, cancellationToken);
        return new ScannedRelocationResult(
            ScannedRelocationStatus.Completed,
            null,
            null,
            movementResult.MovementId,
            evidence?.Id,
            sku.Id,
            source.Id,
            destination.Id,
            command.Quantity);
    }

    private static ScannedRelocationResult Rejected(ScanRejectionCode code, string reason) =>
        new(ScannedRelocationStatus.Rejected, code, reason, null, null, null, null, null, null);
}
