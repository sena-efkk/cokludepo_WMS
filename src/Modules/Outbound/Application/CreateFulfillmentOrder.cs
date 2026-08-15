using Wms.Modules.Facility.Contracts;
using Wms.Modules.MasterData.Contracts;
using Wms.Modules.Outbound.Domain;

namespace Wms.Modules.Outbound.Application;

public enum CreateOrderOutcome
{
    Created = 1,
    AlreadyRecorded = 2,
}

public sealed record CreateFulfillmentOrderLineInput(Guid SkuId, int RequestedQuantity);

public sealed record CreateFulfillmentOrderCommand(
    Guid RequestId,
    string? OrderNumber,
    Guid WarehouseId,
    string? ExternalOrderReference,
    IReadOnlyList<CreateFulfillmentOrderLineInput> Lines);

public sealed record CreateFulfillmentOrderResult(CreateOrderOutcome Outcome, Guid OrderId, string OrderNumber);

public sealed class CreateFulfillmentOrder(
    IOutboundStore store,
    IMasterDataQueryContract masterData,
    IFacilityQueryContract facility)
{
    public async Task<CreateFulfillmentOrderResult> Handle(CreateFulfillmentOrderCommand command, CancellationToken cancellationToken)
    {
        var existing = await store.GetOrderByRequestIdAsync(command.RequestId, cancellationToken);
        if (existing is not null)
        {
            return new CreateFulfillmentOrderResult(CreateOrderOutcome.AlreadyRecorded, existing.Id, existing.OrderNumber);
        }

        var warehouse = await facility.GetWarehouseAsync(command.WarehouseId, cancellationToken)
            ?? throw new InvalidOrderStateException($"Warehouse bulunamadı: {command.WarehouseId}");
        if (!warehouse.IsActive)
        {
            throw new InvalidOrderStateException($"Warehouse aktif değil: {warehouse.Code}");
        }

        if (command.Lines.Count == 0)
        {
            throw new ArgumentException("Order en az bir line içermelidir.");
        }

        foreach (var line in command.Lines)
        {
            var sku = await masterData.GetSkuAsync(line.SkuId, cancellationToken)
                ?? throw new InvalidOrderStateException($"SKU bulunamadı: {line.SkuId}");
            if (!sku.IsActive)
            {
                throw new InvalidOrderStateException($"SKU aktif değil: {sku.Code}");
            }
        }

        var orderNumber = string.IsNullOrWhiteSpace(command.OrderNumber)
            ? GenerateOrderNumber()
            : command.OrderNumber.Trim().ToUpperInvariant();

        if (await store.GetOrderByNumberAsync(orderNumber, cancellationToken) is not null)
        {
            throw new DuplicateOrderNumberException(orderNumber);
        }

        var order = FulfillmentOrder.Create(
            command.RequestId,
            orderNumber,
            command.WarehouseId,
            command.ExternalOrderReference,
            command.Lines
                .Select(l => new OrderLineSpec(l.SkuId, l.RequestedQuantity))
                .ToList());

        await store.AddOrderAsync(order, cancellationToken);

        var outcome = await store.SaveChangesAsync(cancellationToken);
        if (outcome == OutboundSaveOutcome.DuplicateRequest)
        {
            var duplicate = await store.GetOrderByRequestIdAsync(command.RequestId, cancellationToken)
                ?? await store.GetOrderByNumberAsync(orderNumber, cancellationToken);
            if (duplicate is not null)
            {
                return new CreateFulfillmentOrderResult(CreateOrderOutcome.AlreadyRecorded, duplicate.Id, duplicate.OrderNumber);
            }

            throw new InvalidOperationException($"Order oluşturulamadı ama mevcut kayıt bulunamadı: {command.RequestId}");
        }

        return new CreateFulfillmentOrderResult(CreateOrderOutcome.Created, order.Id, order.OrderNumber);
    }

    private static string GenerateOrderNumber() =>
        $"OUT-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
}
