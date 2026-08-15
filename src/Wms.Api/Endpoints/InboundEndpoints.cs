using Wms.Api.Inbound;
using Wms.Modules.Inbound.Application;
using Wms.Modules.Inbound.Domain;

namespace Wms.Api.Endpoints;

public static class InboundEndpoints
{
    public static IEndpointRouteBuilder MapInboundEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/inbound");

        group.MapPost("/receipts", async (CreateReceiptRequest request, CreateReceipt useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new CreateReceiptCommand(
                            request.RequestId ?? Guid.NewGuid(),
                            request.ReceiptNumber,
                            request.WarehouseId,
                            request.ExternalReference,
                            request.SourceType,
                            request.Lines.Select(l => new CreateReceiptLineInput(l.SkuId, l.ExpectedQuantity)).ToList()),
                        ct);
                    return Results.Ok(new CreateReceiptResponse(result.Outcome.ToString(), result.ReceiptId, result.ReceiptNumber));
                },
                ct));

        group.MapGet("/receipts", async (Guid? warehouseId, int limit, ListReceipts useCase, CancellationToken ct) =>
        {
            var receipts = await useCase.Handle(warehouseId, limit <= 0 || limit > 500 ? 100 : limit, ct);
            return Results.Ok(receipts.Select(ReceiptSummaryResponse.From));
        });

        group.MapGet("/summary", async (Guid? warehouseId, GetInboundSummary useCase, CancellationToken ct) =>
        {
            var summary = await useCase.Handle(warehouseId, ct);
            return Results.Ok(summary);
        });

        group.MapGet("/receipts/{id:guid}", async (Guid id, GetReceipt useCase, CancellationToken ct) =>
        {
            var receipt = await useCase.Handle(id, ct);
            return receipt is null ? Results.NotFound() : Results.Ok(ReceiptResponse.From(receipt));
        });

        group.MapPost("/receipts/{id:guid}/receive", async (Guid id, ReceiveItemsRequest request, ReceiveItems useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new ReceiveItemsCommand(
                            request.RequestId ?? Guid.NewGuid(),
                            id,
                            request.ReceiptLineId,
                            request.Quantity,
                            request.ReceivingLocationId,
                            Enum.Parse<ReceivingStockStatus>(request.ReceivingStatus, ignoreCase: true)),
                        ct);
                    return Results.Ok(new ReceiveItemsResponse(
                        result.Outcome.ToString(),
                        result.ReceiveRecordId,
                        result.Disposition.ToString(),
                        result.LineReceivedQuantity,
                        result.PutawayTaskId));
                },
                ct));

        group.MapPost("/receipts/{id:guid}/cancel", async (Guid id, CancelReceipt useCase, GetReceipt getReceipt, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    await useCase.Handle(id, ct);
                    var receipt = await getReceipt.Handle(id, ct);
                    return receipt is null ? Results.NotFound() : Results.Ok(ReceiptResponse.From(receipt));
                },
                ct));

        group.MapGet("/putaway-tasks", async (Guid? warehouseId, string? status, int limit, ListPutawayTasks useCase, CancellationToken ct) =>
        {
            var tasks = await useCase.Handle(
                warehouseId,
                status is null ? null : Enum.Parse<PutawayTaskStatus>(status, ignoreCase: true),
                limit <= 0 || limit > 500 ? 100 : limit,
                ct);
            return Results.Ok(tasks.Select(PutawayTaskResponse.From));
        });

        group.MapGet("/putaway-tasks/{id:guid}", async (Guid id, GetPutawayTask useCase, CancellationToken ct) =>
        {
            var task = await useCase.Handle(id, ct);
            return task is null ? Results.NotFound() : Results.Ok(PutawayTaskResponse.From(task));
        });

        group.MapPost("/putaway-tasks/{id:guid}/start", async (Guid id, StartPutaway useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var task = await useCase.Handle(id, ct);
                    return Results.Ok(PutawayTaskResponse.From(PutawayTaskQuery.From(task)));
                },
                ct));

        group.MapPost("/putaway-tasks/{id:guid}/complete", async (Guid id, CompletePutawayRequest request, CompletePutaway useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new CompletePutawayCommand(
                            id,
                            request.RequestId ?? Guid.NewGuid(),
                            request.SourceScan ?? string.Empty,
                            request.SkuScan ?? string.Empty,
                            request.DestinationScan ?? string.Empty,
                            request.Quantity,
                            request.DeviceId,
                            request.OperatorId),
                        ct);
                    return result.Status == PutawayCompletionStatus.Rejected
                        ? Results.BadRequest(new CompletePutawayResponse(
                            result.Status.ToString(),
                            result.TaskId,
                            result.MovementId,
                            result.RejectionCode,
                            result.RejectionReason))
                        : Results.Ok(new CompletePutawayResponse(
                            result.Status.ToString(),
                            result.TaskId,
                            result.MovementId,
                            result.RejectionCode,
                            result.RejectionReason));
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
        catch (InboundNotFoundException exception)
        {
            return Results.NotFound(exception.Message);
        }
        catch (InvalidReceiptStateException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (InvalidPutawayTaskStateException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (OverReceiptNotAllowedException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (InvalidReceivingLocationException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (DuplicateReceiptNumberException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (PutawaySourceMismatchException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (PutawaySkuMismatchException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (PutawayQuantityMismatchException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (PutawayRejectedException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(exception.Message);
        }
    }
}
