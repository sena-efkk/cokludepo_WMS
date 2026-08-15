using Wms.Api.Transfers;
using Wms.Modules.Transfers.Application;
using Wms.Modules.Transfers.Domain;

namespace Wms.Api.Endpoints;

public static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/transfers");

        group.MapPost("", async (CreateTransferRequest request, CreateTransfer useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new CreateTransferCommand(
                            request.RequestId ?? Guid.NewGuid(),
                            request.TransferNumber,
                            request.SourceWarehouseId,
                            request.DestinationWarehouseId,
                            request.ExternalReference,
                            request.Lines.Select(l => new CreateTransferLineInput(l.SkuId, l.RequestedQuantity)).ToList()),
                        ct);
                    return Results.Ok(new CreateTransferResponse(result.Outcome.ToString(), result.TransferId, result.TransferNumber));
                },
                ct));

        group.MapGet("", async (Guid? warehouseId, int limit, ListTransfers useCase, CancellationToken ct) =>
        {
            var transfers = await useCase.Handle(warehouseId, limit <= 0 || limit > 500 ? 100 : limit, ct);
            return Results.Ok(transfers.Select(TransferSummaryResponse.From));
        });

        group.MapGet("/summary", async (Guid? warehouseId, GetTransfersSummary useCase, CancellationToken ct) =>
        {
            var summary = await useCase.Handle(warehouseId, ct);
            return Results.Ok(summary);
        });

        group.MapGet("/{id:guid}", async (Guid id, GetTransfer useCase, CancellationToken ct) =>
        {
            var transfer = await useCase.Handle(id, ct);
            return transfer is null ? Results.NotFound() : Results.Ok(TransferResponse.From(transfer));
        });

        group.MapPost("/{id:guid}/allocate", async (Guid id, AllocateTransfer useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(id, ct);
                    return result.Outcome == AllocateTransferOutcome.InsufficientStock
                        ? Results.Conflict(new AllocateTransferResponse(result.Outcome.ToString(), result.TransferId, result.OutboundOrderId))
                        : Results.Ok(new AllocateTransferResponse(result.Outcome.ToString(), result.TransferId, result.OutboundOrderId));
                },
                ct));

        group.MapPost("/{id:guid}/ship", async (Guid id, ShipTransferRequest request, ShipTransfer useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new ShipTransferCommand(id, request.TrackingNumber, request.CarrierCode),
                        ct);
                    return Results.Ok(new ShipTransferResponse(
                        result.Outcome.ToString(),
                        result.TransferId,
                        result.ShipmentId,
                        result.ShipmentNumber,
                        result.InboundReceiptId));
                },
                ct));

        group.MapPost("/{id:guid}/receive", async (Guid id, ReceiveTransferRequest request, ReceiveTransfer useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new ReceiveTransferCommand(
                            id,
                            request.RequestId ?? Guid.NewGuid(),
                            request.TransferLineId,
                            request.Quantity,
                            request.ReceivingLocationId,
                            request.ReceivingStatus),
                        ct);
                    return Results.Ok(new ReceiveTransferResponse(
                        result.Outcome.ToString(),
                        result.TransferId,
                        result.TransferLineId,
                        result.LineReceivedQuantity,
                        result.LineInTransitQuantity));
                },
                ct));

        group.MapPost("/{id:guid}/confirm-variance", async (Guid id, ConfirmVarianceRequest request, ConfirmTransferVariance useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new ConfirmVarianceCommand(
                            id,
                            request.RequestId ?? Guid.NewGuid(),
                            request.TransferLineId,
                            request.Quantity,
                            Enum.Parse<TransferDiscrepancyReason>(request.Reason, ignoreCase: true),
                            request.Note),
                        ct);
                    return Results.Ok(new ConfirmVarianceResponse(
                        result.Outcome.ToString(),
                        result.TransferId,
                        result.TransferLineId,
                        result.DiscrepancyId,
                        result.LineInTransitQuantity,
                        result.TransferCompleted));
                },
                ct));

        group.MapPost("/{id:guid}/cancel", async (Guid id, CancelTransfer useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    await useCase.Handle(id, ct);
                    return Results.NoContent();
                },
                ct));

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(Func<Task<IResult>> action, CancellationToken ct)
    {
        try
        {
            return await action();
        }
        catch (TransferNotFoundException exception)
        {
            return Results.NotFound(exception.Message);
        }
        catch (InvalidTransferStateException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (OverReceiptRejectedException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (DuplicateTransferNumberException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(exception.Message);
        }
    }
}
