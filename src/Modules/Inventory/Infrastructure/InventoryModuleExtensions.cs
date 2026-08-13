using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wms.Modules.Inventory.Application;
using Wms.Modules.Inventory.Application.Accuracy;
using Wms.Modules.Inventory.Application.Accuracy.CycleCounting;
using Wms.Modules.Inventory.Application.Accuracy.Reconciliation;
using Wms.Modules.Inventory.Application.Accuracy.Scanning;
using Wms.Modules.Inventory.Contracts;
using Wms.Modules.Inventory.Infrastructure.Persistence;

namespace Wms.Modules.Inventory.Infrastructure;

public static class InventoryModuleExtensions
{
    public static IServiceCollection AddInventoryModule(
        this IServiceCollection services,
        string? connectionString,
        IConfiguration? configuration = null)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<InventoryDbContext>(options =>
                options
                    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "inventory"))
                    .UseSnakeCaseNamingConvention());
        }

        if (configuration is not null)
        {
            services.Configure<RiskPolicyOptions>(configuration.GetSection("Inventory:RiskPolicy"));
        }

        services.AddSingleton(sp => new InventoryRiskAnalyzer(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RiskPolicyOptions>>().Value));

        services.AddScoped<IInventoryStore, InventoryStore>();
        services.AddScoped<RecordOpeningBalance>();
        services.AddScoped<Reserve>();
        services.AddScoped<ReleaseReservation>();
        services.AddScoped<ConsumeReservation>();
        services.AddScoped<RelocateStock>();
        services.AddScoped<ChangeInventoryStatus>();
        services.AddScoped<ExecuteScannedRelocation>();
        services.AddScoped<GetMovement>();
        services.AddScoped<ListMovements>();
        services.AddScoped<ReportPickNotFound>();
        services.AddScoped<GetAccuracySignals>();
        services.AddScoped<GetSignalsForSkuLocation>();
        services.AddScoped<GetRecentNotFoundSignals>();
        services.AddScoped<GetLocationRiskAssessment>();
        services.AddScoped<ListRiskAssessments>();
        services.AddScoped<GetAbcDeadSummary>();
        services.AddScoped<EvaluateCycleCountCandidates>();
        services.AddScoped<StartCycleCount>();
        services.AddScoped<CompleteCycleCount>();
        services.AddScoped<CancelCycleCount>();
        services.AddScoped<GetCycleCountTask>();
        services.AddScoped<GetCycleCountResult>();
        services.AddScoped<ListCycleCountTasks>();
        services.AddScoped<GetCycleCountQueue>();
        services.AddScoped<ApproveReconciliation>();
        services.AddScoped<RejectReconciliation>();
        services.AddScoped<CancelReconciliation>();
        services.AddScoped<GetReconciliation>();
        services.AddScoped<ListReconciliations>();
        services.AddScoped<GetWarehouseBalances>();
        services.AddScoped<GetWarehouseSkuSummary>();
        services.AddScoped<GetReservation>();
        services.AddScoped<GetLedger>();
        services.AddScoped<IInventoryContract, InventoryContractAdapter>();

        return services;
    }
}
