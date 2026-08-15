using System.Security.Cryptography;
using System.Text;
using Wms.Modules.Facility.Contracts;
using Wms.Modules.MasterData.Contracts;
using Wms.Modules.Transfers.Domain;

namespace Wms.Modules.Transfers.Application;

public enum CreateTransferOutcome
{
    Created = 1,
    AlreadyRecorded = 2,
}

public sealed record CreateTransferLineInput(Guid SkuId, int RequestedQuantity);

public sealed record CreateTransferCommand(
    Guid RequestId,
    string? TransferNumber,
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    string? ExternalReference,
    IReadOnlyList<CreateTransferLineInput> Lines);

public sealed record CreateTransferResult(CreateTransferOutcome Outcome, Guid TransferId, string TransferNumber);

public sealed class CreateTransfer(
    ITransferStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility)
{
    public async Task<CreateTransferResult> Handle(CreateTransferCommand command, CancellationToken cancellationToken)
    {
        var existing = await store.GetTransferByRequestIdAsync(command.RequestId, cancellationToken);
        if (existing is not null)
        {
            return new CreateTransferResult(CreateTransferOutcome.AlreadyRecorded, existing.Id, existing.TransferNumber);
        }

        if (command.SourceWarehouseId == command.DestinationWarehouseId)
        {
            throw new InvalidTransferStateException("Source ve destination warehouse aynı olamaz — aynı depo içi hareket RelocateStock işidir.");
        }

        var source = await facility.GetWarehouseAsync(command.SourceWarehouseId, cancellationToken)
            ?? throw new InvalidTransferStateException($"Source warehouse bulunamadı: {command.SourceWarehouseId}");
        if (!source.IsActive)
        {
            throw new InvalidTransferStateException($"Source warehouse aktif değil: {source.Code}");
        }

        var destination = await facility.GetWarehouseAsync(command.DestinationWarehouseId, cancellationToken)
            ?? throw new InvalidTransferStateException($"Destination warehouse bulunamadı: {command.DestinationWarehouseId}");
        if (!destination.IsActive)
        {
            throw new InvalidTransferStateException($"Destination warehouse aktif değil: {destination.Code}");
        }

        if (command.Lines.Count == 0)
        {
            throw new ArgumentException("Transfer en az bir line içermelidir.");
        }

        foreach (var line in command.Lines)
        {
            var sku = await masterData.GetSkuAsync(line.SkuId, cancellationToken)
                ?? throw new InvalidTransferStateException($"SKU bulunamadı: {line.SkuId}");
            if (!sku.IsActive)
            {
                throw new InvalidTransferStateException($"SKU aktif değil: {sku.Code}");
            }
        }

        var transferNumber = string.IsNullOrWhiteSpace(command.TransferNumber)
            ? GenerateTransferNumber()
            : command.TransferNumber.Trim().ToUpperInvariant();

        if (await store.GetTransferByNumberAsync(transferNumber, cancellationToken) is not null)
        {
            throw new DuplicateTransferNumberException(transferNumber);
        }

        var transfer = TransferOrder.Create(
            command.RequestId,
            transferNumber,
            command.SourceWarehouseId,
            command.DestinationWarehouseId,
            command.ExternalReference,
            command.Lines
                .Select(l => new TransferLineSpec(l.SkuId, l.RequestedQuantity))
                .ToList());

        await store.AddTransferAsync(transfer, cancellationToken);

        var outcome = await store.SaveChangesAsync(cancellationToken);
        if (outcome == TransferSaveOutcome.DuplicateRequest)
        {
            var duplicate = await store.GetTransferByRequestIdAsync(command.RequestId, cancellationToken)
                ?? await store.GetTransferByNumberAsync(transferNumber, cancellationToken);
            if (duplicate is not null)
            {
                return new CreateTransferResult(CreateTransferOutcome.AlreadyRecorded, duplicate.Id, duplicate.TransferNumber);
            }

            throw new InvalidOperationException($"Transfer oluşturulamadı ama kayıt bulunamadı: {command.RequestId}");
        }

        return new CreateTransferResult(CreateTransferOutcome.Created, transfer.Id, transfer.TransferNumber);
    }

    internal static Guid DeriveChildRequestId(Guid transferId, string purpose)
    {
        var bytes = Encoding.UTF8.GetBytes($"{transferId:N}:{purpose}");
        var hash = MD5.HashData(bytes);
        return new Guid(hash);
    }

    private static string GenerateTransferNumber() =>
        $"TRF-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
}
