using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wms.Modules.Outbound.Application;
using Wms.Modules.Outbound.Contracts;
using Wms.Modules.Outbound.Infrastructure.Persistence;

namespace Wms.Modules.Outbound.Infrastructure;

public static class OutboundModuleExtensions
{
    public static IServiceCollection AddOutboundModule(
        this IServiceCollection services,
        string? connectionString,
        IConfiguration? configuration = null)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<OutboundDbContext>(options =>
                options
                    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "outbound"))
                    .UseSnakeCaseNamingConvention());
        }

        services.AddScoped<OutboundStore>();
        services.AddScoped<IOutboundStore>(sp => sp.GetRequiredService<OutboundStore>());
        services.AddScoped<Wms.Integration.Outbox.IOutboxStore>(sp => sp.GetRequiredService<OutboundStore>());
        services.AddScoped<IOutboundContract, OutboundContractAdapter>();
        services.AddScoped<CreateFulfillmentOrder>();
        services.AddScoped<AllocateOrder>();
        services.AddScoped<StartPick>();
        services.AddScoped<ConfirmPick>();
        services.AddScoped<MarkPickNotFound>();
        services.AddScoped<PackOrder>();
        services.AddScoped<ShipOrder>();
        services.AddScoped<CancelOrder>();
        services.AddScoped<GetOrder>();
        services.AddScoped<GetOutboundSummary>();
        services.AddScoped<ListOrders>();
        services.AddScoped<GetPickTask>();
        services.AddScoped<ListPickTasks>();

        return services;
    }
}
