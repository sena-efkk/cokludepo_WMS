using Wms.Modules.Facility.Contracts;
using Wms.Modules.Inbound.Domain;
using Wms.Modules.MasterData.Contracts;

namespace Wms.Modules.Inbound.Application;

public enum CreateReceiptOutcome
{
    Created = 1,
    AlreadyRecorded = 2,
}

public sealed record CreateReceiptLineInput(Guid SkuId, int ExpectedQuantity);

public sealed record CreateReceiptCommand(
    Guid RequestId,
    string? ReceiptNumber,
    Guid WarehouseId,
    string? ExternalReference,
    string? SourceType,
    IReadOnlyList<CreateReceiptLineInput> Lines);

public sealed record CreateReceiptResult(CreateReceiptOutcome Outcome, Guid ReceiptId, string ReceiptNumber);

public sealed class CreateReceipt(
    IInboundStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility)
{
    public async Task<CreateReceiptResult> Handle(CreateReceiptCommand command, CancellationToken cancellationToken)
    {
        var existing = await store.GetReceiptByRequestIdAsync(command.RequestId, cancellationToken);
        if (existing is not null)
        {
            return new CreateReceiptResult(CreateReceiptOutcome.AlreadyRecorded, existing.Id, existing.ReceiptNumber);
        }

        var warehouse = await facility.GetWarehouseAsync(command.WarehouseId, cancellationToken)
            ?? throw new InvalidReceiptStateException($"Warehouse bulunamadı: {command.WarehouseId}");
        if (!warehouse.IsActive)
        {
            throw new InvalidReceiptStateException($"Warehouse aktif değil: {warehouse.Code}");
        }

        if (command.Lines.Count == 0)
        {
            throw new ArgumentException("Receipt en az bir line içermelidir.");
        }

        foreach (var lineInput in command.Lines)
        {
            var sku = await masterData.GetSkuAsync(lineInput.SkuId, cancellationToken)
                ?? throw new InvalidReceiptStateException($"SKU bulunamadı: {lineInput.SkuId}");
            if (!sku.IsActive)
            {
                throw new InvalidReceiptStateException($"SKU aktif değil: {sku.Code}");
            }
        }

        var receiptNumber = string.IsNullOrWhiteSpace(command.ReceiptNumber)
            ? GenerateReceiptNumber()
            : command.ReceiptNumber.Trim().ToUpperInvariant();

        if (await store.GetReceiptByNumberAsync(receiptNumber, cancellationToken) is not null)
        {
            throw new DuplicateReceiptNumberException(receiptNumber);
        }

        var receipt = InboundReceipt.Create(
            command.RequestId,
            receiptNumber,
            command.WarehouseId,
            command.ExternalReference,
            command.SourceType,
            command.Lines
                .Select(l => new ReceiptLineSpec(l.SkuId, l.ExpectedQuantity))
                .ToList());

        await store.AddReceiptAsync(receipt, cancellationToken);

        var outcome = await store.SaveChangesAsync(cancellationToken);
        if (outcome == InboundSaveOutcome.DuplicateRequest)
        {
            var duplicate = await store.GetReceiptByRequestIdAsync(command.RequestId, cancellationToken)
                ?? await store.GetReceiptByNumberAsync(receiptNumber, cancellationToken);
            if (duplicate is not null)
            {
                return new CreateReceiptResult(CreateReceiptOutcome.AlreadyRecorded, duplicate.Id, duplicate.ReceiptNumber);
            }

            throw new InvalidOperationException($"Receipt oluşturulamadı ama mevcut kayıt bulunamadı: {command.RequestId}");
        }

        return new CreateReceiptResult(CreateReceiptOutcome.Created, receipt.Id, receipt.ReceiptNumber);
    }

    private static string GenerateReceiptNumber() =>
        $"INB-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
}
