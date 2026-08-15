using Wms.Api.Outbound;
using Wms.Modules.Outbound.Application;

namespace Wms.Api.Endpoints;

public static class OutboundEndpoints
{
    public static IEndpointRouteBuilder MapOutboundEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/outbound");

        group.MapPost("/orders", async (CreateOrderRequest request, CreateFulfillmentOrder useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new CreateFulfillmentOrderCommand(
                            request.RequestId ?? Guid.NewGuid(),
                            request.OrderNumber,
                            request.WarehouseId,
                            request.ExternalOrderReference,
                            request.Lines.Select(l => new CreateFulfillmentOrderLineInput(l.SkuId, l.RequestedQuantity)).ToList()),
                        ct);
                    return Results.Ok(new CreateOrderResponse(result.Outcome.ToString(), result.OrderId, result.OrderNumber));
                },
                ct));

        group.MapGet("/orders", async (Guid? warehouseId, int limit, ListOrders useCase, CancellationToken ct) =>
        {
            var orders = await useCase.Handle(warehouseId, limit <= 0 || limit > 500 ? 100 : limit, ct);
            return Results.Ok(orders.Select(OrderSummaryResponse.From));
        });

        group.MapGet("/summary", async (Guid? warehouseId, GetOutboundSummary useCase, CancellationToken ct) =>
        {
            var summary = await useCase.Handle(warehouseId, ct);
            return Results.Ok(summary);
        });

        group.MapGet("/orders/{id:guid}", async (Guid id, GetOrder useCase, CancellationToken ct) =>
        {
            var order = await useCase.Handle(id, ct);
            return order is null ? Results.NotFound() : Results.Ok(OrderResponse.From(order));
        });

        group.MapPost("/orders/{id:guid}/allocate", async (Guid id, AllocateOrder useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(id, ct);
                    return result.Outcome == AllocateOrderOutcome.InsufficientStock
                        ? Results.Conflict(new AllocateOrderResponse(result.Outcome.ToString(), result.OrderId))
                        : Results.Ok(new AllocateOrderResponse(result.Outcome.ToString(), result.OrderId));
                },
                ct));

        group.MapGet("/pick-tasks", async (Guid? warehouseId, string? status, int limit, ListPickTasks useCase, CancellationToken ct) =>
        {
            var tasks = await useCase.Handle(
                warehouseId,
                status is null ? null : Enum.Parse<Wms.Modules.Outbound.Domain.PickTaskStatus>(status, ignoreCase: true),
                limit <= 0 || limit > 500 ? 100 : limit,
                ct);
            return Results.Ok(tasks.Select(PickTaskResponse.From));
        });

        group.MapPost("/pick-tasks/{id:guid}/start", async (Guid id, StartPick useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var task = await useCase.Handle(id, ct);
                    return Results.Ok(PickTaskResponse.From(PickTaskQuery.From(task)));
                },
                ct));

        group.MapPost("/pick-tasks/{id:guid}/confirm", async (Guid id, ConfirmPickRequest request, ConfirmPick useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new ConfirmPickCommand(id, request.LocationScan ?? string.Empty, request.SkuScan ?? string.Empty, request.Quantity),
                        ct);
                    return Results.Ok(new ConfirmPickResponse(result.TaskId, result.TaskCompleted, result.PickedQuantity, result.RemainingQuantity));
                },
                ct));

        group.MapPost("/pick-tasks/{id:guid}/not-found", async (Guid id, NotFoundPickRequest request, MarkPickNotFound useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new MarkPickNotFoundCommand(id, request.RequestId ?? Guid.NewGuid()),
                        ct);
                    return Results.Ok(new { result.TaskId, result.OrderPickException, result.SignalRequestId });
                },
                ct));

        group.MapPost("/orders/{id:guid}/pack", async (Guid id, PackOrderRequest request, PackOrder useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new PackOrderCommand(id, request.RequestId ?? Guid.NewGuid()),
                        ct);
                    return Results.Ok(new PackOrderResponse(result.Outcome.ToString(), result.OrderId, result.PackageId, result.PackageNumber));
                },
                ct));

        group.MapPost("/orders/{id:guid}/ship", async (Guid id, ShipOrderRequest request, ShipOrder useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new ShipOrderCommand(id, request.RequestId ?? Guid.NewGuid(), request.TrackingNumber, request.CarrierCode),
                        ct);
                    return Results.Ok(new ShipOrderResponse(result.Outcome.ToString(), result.OrderId, result.ShipmentId, result.ShipmentNumber));
                },
                ct));

        group.MapPost("/orders/{id:guid}/cancel", async (Guid id, CancelOrder useCase, CancellationToken ct) =>
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
        catch (OutboundNotFoundException exception)
        {
            return Results.NotFound(exception.Message);
        }
        catch (InvalidOrderStateException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (InvalidPickTaskStateException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (PickLocationMismatchException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (PickSkuMismatchException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (PickQuantityExceededException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (DuplicateOrderNumberException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(exception.Message);
        }
    }
}
