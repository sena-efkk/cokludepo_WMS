using Wms.Api.Inventory;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Domain;
using Wms.Modules.Inventory.Domain.Accuracy;
using Wms.Modules.Inventory.Domain.Accuracy.Reconciliation;

namespace Wms.Api.Endpoints;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/inventory");

        group.MapPost("/opening-balances", async (RecordOpeningBalanceRequest request, RecordOpeningBalance useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new RecordOpeningBalanceCommand(
                            request.RequestId ?? Guid.NewGuid(),
                            request.SkuId,
                            request.WarehouseId,
                            request.LocationId,
                            Enum.Parse<InventoryStatus>(request.Status, ignoreCase: true),
                            request.Quantity),
                        ct);
                    return Results.Ok(new OpeningBalanceResponse(result.Outcome.ToString(), result.RequestId));
                },
                ct));

        group.MapGet("/warehouses/{warehouseId:guid}/balances", async (Guid warehouseId, Guid? skuId, Guid? locationId, bool includeEmpty, GetWarehouseBalances useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var balances = await useCase.Handle(warehouseId, skuId, locationId, includeEmpty, ct);
                    return Results.Ok(balances.Select(BalanceResponse.From));
                },
                ct));

        group.MapGet("/warehouses/{warehouseId:guid}/skus/{skuId:guid}", async (Guid warehouseId, Guid skuId, GetWarehouseSkuSummary useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var summary = await useCase.Handle(warehouseId, skuId, ct);
                    return Results.Ok(WarehouseSkuResponse.From(summary));
                },
                ct));

        group.MapPost("/reservations", async (ReserveRequest request, Reserve useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var reservation = await useCase.Handle(
                        new ReserveCommand(
                            request.RequestId ?? Guid.NewGuid(),
                            request.SkuId,
                            request.WarehouseId,
                            request.Quantity,
                            request.Purpose ?? string.Empty),
                        ct);
                    return Results.Ok(ReservationResponse.From(reservation));
                },
                ct));

        group.MapGet("/reservations/{id:guid}", async (Guid id, GetReservation useCase, CancellationToken ct) =>
        {
            var reservation = await useCase.Handle(id, ct);
            return reservation is null ? Results.NotFound() : Results.Ok(ReservationResponse.From(reservation));
        });

        group.MapPost("/reservations/{id:guid}/release", async (Guid id, ReleaseReservation useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    await useCase.Handle(id, ct);
                    return Results.NoContent();
                },
                ct));

        group.MapPost("/reservations/{id:guid}/consume", async (Guid id, ConsumeReservation useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    await useCase.Handle(id, ct);
                    return Results.NoContent();
                },
                ct));

        group.MapGet("/ledger", async (Guid? warehouseId, Guid? skuId, Guid? locationId, int limit, GetLedger useCase, CancellationToken ct) =>
        {
            var entries = await useCase.Handle(warehouseId, skuId, locationId, limit <= 0 || limit > 500 ? 100 : limit, ct);
            return Results.Ok(entries.Select(LedgerEntryResponse.From));
        });

        group.MapPost("/movements/relocate", async (RelocateRequest request, RelocateStock useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new RelocateCommand(
                            request.RequestId ?? Guid.NewGuid(),
                            request.SkuId,
                            request.WarehouseId,
                            request.SourceLocationId,
                            request.DestinationLocationId,
                            request.Quantity),
                        ct);
                    return Results.Ok(new MovementResultResponse(result.Outcome.ToString(), result.MovementId));
                },
                ct));

        group.MapPost("/movements/change-status", async (ChangeStatusRequest request, ChangeInventoryStatus useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new ChangeStatusCommand(
                            request.RequestId ?? Guid.NewGuid(),
                            request.SkuId,
                            request.WarehouseId,
                            request.LocationId,
                            Enum.Parse<InventoryStatus>(request.FromStatus, ignoreCase: true),
                            Enum.Parse<InventoryStatus>(request.ToStatus, ignoreCase: true),
                            request.Quantity),
                        ct);
                    return Results.Ok(new MovementResultResponse(result.Outcome.ToString(), result.MovementId));
                },
                ct));

        group.MapPost("/movements/scanned-relocation", async (ScannedRelocationRequest request, Wms.Modules.Inventory.Application.Accuracy.Scanning.ExecuteScannedRelocation useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new Wms.Modules.Inventory.Application.Accuracy.Scanning.ScannedRelocationCommand(
                            request.RequestId ?? Guid.NewGuid(),
                            request.WarehouseId,
                            request.SourceLocationScan ?? string.Empty,
                            request.SkuScan ?? string.Empty,
                            request.DestinationLocationScan ?? string.Empty,
                            request.Quantity,
                            request.DeviceId,
                            request.OperatorId),
                        ct);
                    return result.Status == Wms.Modules.Inventory.Application.Accuracy.Scanning.ScannedRelocationStatus.Rejected
                        ? Results.BadRequest(ScannedRelocationResponse.From(result))
                        : Results.Ok(ScannedRelocationResponse.From(result));
                },
                ct));

        group.MapGet("/movements/{id:guid}", async (Guid id, GetMovement useCase, CancellationToken ct) =>
        {
            var movement = await useCase.Handle(id, ct);
            return movement is null ? Results.NotFound() : Results.Ok(MovementResponse.From(movement));
        });

        group.MapGet("/movements", async (Guid? warehouseId, Guid? skuId, int limit, ListMovements useCase, CancellationToken ct) =>
        {
            var movements = await useCase.Handle(warehouseId, skuId, limit <= 0 || limit > 500 ? 100 : limit, ct);
            return Results.Ok(movements.Select(MovementResponse.From));
        });

        group.MapPost("/accuracy/pick-not-found", async (ReportPickNotFoundRequest request, Wms.Modules.Inventory.Application.Accuracy.ReportPickNotFound useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new Wms.Modules.Inventory.Application.Accuracy.ReportPickNotFoundCommand(
                            request.RequestId ?? Guid.NewGuid(),
                            request.SkuId,
                            request.WarehouseId,
                            request.LocationId,
                            AccuracySourceType.Pick,
                            request.SourceReferenceId is null ? null : Guid.Parse(request.SourceReferenceId),
                            request.OccurredAt),
                        ct);
                    return Results.Ok(new ReportSignalResultResponse(result.Outcome.ToString(), result.SignalId));
                },
                ct));

        group.MapGet("/accuracy/signals", async (Guid? warehouseId, Guid? skuId, Guid? locationId, string? signalType, DateTime? from, DateTime? to, int limit, Wms.Modules.Inventory.Application.Accuracy.GetAccuracySignals useCase, CancellationToken ct) =>
        {
            var filter = new Wms.Modules.Inventory.Application.Accuracy.AccuracySignalFilter(
                warehouseId,
                skuId,
                locationId,
                signalType is null ? null : Enum.Parse<AccuracySignalType>(signalType, ignoreCase: true),
                from,
                to);
            var signals = await useCase.Handle(filter, limit <= 0 || limit > 500 ? 100 : limit, ct);
            return Results.Ok(signals.Select(AccuracySignalResponse.From));
        });

        group.MapGet("/accuracy/signals/sku-location", async (Guid warehouseId, Guid skuId, Guid locationId, string? signalType, DateTime? from, DateTime? to, Wms.Modules.Inventory.Application.Accuracy.GetSignalsForSkuLocation useCase, CancellationToken ct) =>
        {
            var signals = await useCase.Handle(
                warehouseId,
                skuId,
                locationId,
                signalType is null ? null : Enum.Parse<AccuracySignalType>(signalType, ignoreCase: true),
                from,
                to,
                ct);
            return Results.Ok(signals.Select(AccuracySignalResponse.From));
        });

        group.MapGet("/accuracy/signals/recent-not-found", async (Guid? warehouseId, int days, int limit, Wms.Modules.Inventory.Application.Accuracy.GetRecentNotFoundSignals useCase, CancellationToken ct) =>
        {
            var signals = await useCase.Handle(warehouseId, days <= 0 ? 30 : days, limit <= 0 || limit > 500 ? 100 : limit, ct);
            return Results.Ok(signals.Select(AccuracySignalResponse.From));
        });

        group.MapGet("/accuracy/risk", async (Guid? warehouseId, Guid? skuId, Guid? locationId, string? riskLevel, int limit, Wms.Modules.Inventory.Application.Accuracy.ListRiskAssessments useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var assessments = await useCase.Handle(
                        warehouseId,
                        skuId,
                        locationId,
                        riskLevel is null ? null : Enum.Parse<Wms.Modules.Inventory.Domain.Accuracy.RiskLevel>(riskLevel, ignoreCase: true),
                        limit <= 0 || limit > 500 ? 100 : limit,
                        ct);
                    return Results.Ok(assessments.Select(RiskAssessmentResponse.From));
                },
                ct));

        group.MapGet("/accuracy/risk/{warehouseId:guid}/{skuId:guid}/{locationId:guid}", async (Guid warehouseId, Guid skuId, Guid locationId, Wms.Modules.Inventory.Application.Accuracy.GetLocationRiskAssessment useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var assessment = await useCase.Handle(warehouseId, skuId, locationId, ct);
                    return Results.Ok(RiskAssessmentResponse.From(assessment));
                },
                ct));

        group.MapGet("/accuracy/high-risk", async (Guid? warehouseId, int limit, Wms.Modules.Inventory.Application.Accuracy.ListRiskAssessments useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var assessments = await useCase.Handle(
                        warehouseId,
                        null,
                        null,
                        Wms.Modules.Inventory.Domain.Accuracy.RiskLevel.Red,
                        limit <= 0 || limit > 500 ? 50 : limit,
                        ct);
                    return Results.Ok(assessments.Select(RiskAssessmentResponse.From));
                },
                ct));

        group.MapGet("/accuracy/abc-dead-summary", async (Guid warehouseId, Wms.Modules.Inventory.Application.Accuracy.GetAbcDeadSummary useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var summary = await useCase.Handle(warehouseId, ct);
                    return Results.Ok(AbcDeadSummaryResponse.From(summary));
                },
                ct));

        group.MapGet("/accuracy/summary", async (Guid? warehouseId, Wms.Modules.Inventory.Application.Accuracy.GetAccuracySummary useCase, CancellationToken ct) =>
        {
            var summary = await useCase.Handle(warehouseId, ct);
            return Results.Ok(summary);
        });

        var cycleCounts = group.MapGroup("/accuracy/cycle-counts");

        cycleCounts.MapPost("/evaluate", async (Guid? warehouseId, Wms.Modules.Inventory.Application.Accuracy.CycleCounting.EvaluateCycleCountCandidates useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(warehouseId, ct);
                    return Results.Ok(new EvaluateCycleCountsResponse(result.Created, result.Skipped));
                },
                ct));

        cycleCounts.MapGet("/queue", async (Guid? warehouseId, int limit, Wms.Modules.Inventory.Application.Accuracy.CycleCounting.GetCycleCountQueue useCase, CancellationToken ct) =>
        {
            var tasks = await useCase.Handle(warehouseId, limit <= 0 || limit > 500 ? 100 : limit, ct);
            return Results.Ok(tasks.Select(t => CycleCountTaskResponse.From(t, null)));
        });

        cycleCounts.MapGet("", async (Guid? warehouseId, string? status, string? priority, int limit, Wms.Modules.Inventory.Application.Accuracy.CycleCounting.ListCycleCountTasks useCase, CancellationToken ct) =>
        {
            var tasks = await useCase.Handle(
                warehouseId,
                status is null ? null : Enum.Parse<Wms.Modules.Inventory.Domain.Accuracy.CycleCounting.CycleCountTaskStatus>(status, ignoreCase: true),
                priority is null ? null : Enum.Parse<Wms.Modules.Inventory.Domain.Accuracy.CycleCounting.CycleCountPriority>(priority, ignoreCase: true),
                limit <= 0 || limit > 500 ? 100 : limit,
                ct);
            return Results.Ok(tasks.Select(t => CycleCountTaskResponse.From(t, null)));
        });

        cycleCounts.MapGet("/{id:guid}", async (Guid id, Wms.Modules.Inventory.Application.Accuracy.CycleCounting.GetCycleCountTask getTask, Wms.Modules.Inventory.Application.Accuracy.CycleCounting.GetCycleCountResult getResult, CancellationToken ct) =>
        {
            var task = await getTask.Handle(id, ct);
            if (task is null)
            {
                return Results.NotFound();
            }

            var result = await getResult.Handle(id, ct);
            return Results.Ok(CycleCountTaskResponse.From(task, result));
        });

        cycleCounts.MapPost("/{id:guid}/start", async (Guid id, StartCycleCountRequest request, Wms.Modules.Inventory.Application.Accuracy.CycleCounting.StartCycleCount useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var task = await useCase.Handle(id, request.AssignedTo, ct);
                    return Results.Ok(CycleCountTaskResponse.From(task, null));
                },
                ct));

        cycleCounts.MapPost("/{id:guid}/complete", async (Guid id, CompleteCycleCountRequest request, Wms.Modules.Inventory.Application.Accuracy.CycleCounting.CompleteCycleCount useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(id, request.CountedQuantity, request.CountedBy, ct);
                    return Results.Ok(CycleCountResultResponse.From(result));
                },
                ct));

        cycleCounts.MapPost("/{id:guid}/cancel", async (Guid id, Wms.Modules.Inventory.Application.Accuracy.CycleCounting.CancelCycleCount useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    await useCase.Handle(id, ct);
                    return Results.NoContent();
                },
                ct));

        var reconciliations = group.MapGroup("/accuracy/reconciliations");

        reconciliations.MapGet("", async (Guid? warehouseId, string? status, int limit, Wms.Modules.Inventory.Application.Accuracy.Reconciliation.ListReconciliations useCase, CancellationToken ct) =>
        {
            var items = await useCase.Handle(
                warehouseId,
                status is null ? null : Enum.Parse<ReconciliationStatus>(status, ignoreCase: true),
                limit <= 0 || limit > 500 ? 100 : limit,
                ct);
            return Results.Ok(items.Select(ReconciliationResponse.From));
        });

        reconciliations.MapGet("/{id:guid}", async (Guid id, Wms.Modules.Inventory.Application.Accuracy.Reconciliation.GetReconciliation useCase, CancellationToken ct) =>
        {
            var reconciliation = await useCase.Handle(id, ct);
            return reconciliation is null ? Results.NotFound() : Results.Ok(ReconciliationResponse.From(reconciliation));
        });

        reconciliations.MapPost("/{id:guid}/approve", async (Guid id, ApproveReconciliationRequest request, Wms.Modules.Inventory.Application.Accuracy.Reconciliation.ApproveReconciliation useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    var result = await useCase.Handle(
                        new Wms.Modules.Inventory.Application.Accuracy.Reconciliation.ApproveReconciliationCommand(
                            id,
                            request.RequestId ?? Guid.NewGuid(),
                            request.Reason is null
                                ? AdjustmentReason.CycleCountVariance
                                : Enum.Parse<AdjustmentReason>(request.Reason, ignoreCase: true),
                            request.ResolvedBy,
                            request.ResolutionNote,
                            request.Force),
                        ct);
                    return Results.Ok(new ApprovalResultResponse(result.Outcome.ToString(), result.AdjustmentId));
                },
                ct));

        reconciliations.MapPost("/{id:guid}/reject", async (Guid id, RejectReconciliationRequest request, Wms.Modules.Inventory.Application.Accuracy.Reconciliation.RejectReconciliation useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    await useCase.Handle(id, request.ResolvedBy, request.ResolutionNote, ct);
                    return Results.NoContent();
                },
                ct));

        reconciliations.MapPost("/{id:guid}/cancel", async (Guid id, RejectReconciliationRequest request, Wms.Modules.Inventory.Application.Accuracy.Reconciliation.CancelReconciliation useCase, CancellationToken ct) =>
            await HandleAsync(
                async () =>
                {
                    await useCase.Handle(id, request.ResolvedBy, request.ResolutionNote, ct);
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
        catch (InventoryNotFoundException exception)
        {
            return Results.NotFound(exception.Message);
        }
        catch (Wms.Modules.Inventory.Application.Accuracy.CycleCounting.InvalidCycleCountStateException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (Wms.Modules.Inventory.Application.Accuracy.Reconciliation.InvalidReconciliationStateException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (Wms.Modules.Inventory.Application.Accuracy.Reconciliation.AdjustmentConflictException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (Wms.Modules.Inventory.Application.Accuracy.Reconciliation.LargeVarianceException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (InsufficientInventoryException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (InvalidReservationStateException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (SkuValidationException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (WarehouseValidationException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (LocationValidationException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(exception.Message);
        }
    }
}
