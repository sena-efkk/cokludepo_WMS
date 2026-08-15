using Wms.Modules.Outbound.Domain;

namespace Wms.Modules.Outbound.Application;

public enum PackOrderOutcome
{
    Packed = 1,
    AlreadyPacked = 2,
}

public sealed record PackOrderCommand(Guid OrderId, Guid RequestId);

public sealed record PackOrderResult(PackOrderOutcome Outcome, Guid OrderId, Guid PackageId, string PackageNumber);

public sealed class PackOrder(
    IOutboundStore store)
{
    public async Task<PackOrderResult> Handle(PackOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await store.GetOrderAsync(command.OrderId, cancellationToken)
            ?? throw new OrderNotFoundException(command.OrderId);

        if (order.Status == OrderStatus.Packed)
        {
            var existingPackage = await store.GetPackageByOrderAsync(order.Id, cancellationToken);
            if (existingPackage is not null)
            {
                return new PackOrderResult(PackOrderOutcome.AlreadyPacked, order.Id, existingPackage.Id, existingPackage.PackageNumber);
            }
        }

        if (order.Status != OrderStatus.Picked)
        {
            throw new InvalidOrderStateException(
                $"Order yalnızca PICKED durumundayken pack edilebilir. Mevcut: {order.Status}");
        }

        var existing = await store.GetPackageByRequestIdAsync(command.RequestId, cancellationToken);
        if (existing is not null)
        {
            return new PackOrderResult(PackOrderOutcome.AlreadyPacked, order.Id, existing.Id, existing.PackageNumber);
        }

        var package = Package.Create(
            order.Id,
            command.RequestId,
            $"PAK-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}");

        await store.BeginTransactionAsync(cancellationToken);
        try
        {
            var fresh = await store.GetOrderAsync(command.OrderId, cancellationToken)
                ?? throw new OrderNotFoundException(command.OrderId);

            if (fresh.Status != OrderStatus.Picked)
            {
                throw new InvalidOrderStateException(
                    $"Order yalnızca PICKED durumundayken pack edilebilir. Mevcut: {fresh.Status}");
            }

            fresh.MarkPacked();
            await store.AddPackageAsync(package, cancellationToken);

            var outcome = await store.SaveChangesAsync(cancellationToken);
            await store.CommitTransactionAsync(cancellationToken);

            if (outcome == OutboundSaveOutcome.DuplicateRequest)
            {
                var winner = await store.GetPackageByRequestIdAsync(command.RequestId, cancellationToken);
                if (winner is not null)
                {
                    return new PackOrderResult(PackOrderOutcome.AlreadyPacked, order.Id, winner.Id, winner.PackageNumber);
                }

                throw new InvalidOperationException($"Pack çakıştı ama package bulunamadı: {command.RequestId}");
            }

            return new PackOrderResult(PackOrderOutcome.Packed, order.Id, package.Id, package.PackageNumber);
        }
        catch
        {
            await store.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
