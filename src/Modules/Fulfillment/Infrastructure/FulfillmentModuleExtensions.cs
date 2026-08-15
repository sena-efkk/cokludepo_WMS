using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wms.Modules.Fulfillment.Application;
using Wms.Modules.Fulfillment.Application.Optimization;
using Wms.Modules.Fulfillment.Infrastructure.Persistence;

namespace Wms.Modules.Fulfillment.Infrastructure;

public static class FulfillmentModuleExtensions
{
    public static IServiceCollection AddFulfillmentModule(
        this IServiceCollection services,
        string? connectionString,
        IConfiguration? configuration = null)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<FulfillmentDbContext>(options =>
                options
                    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "fulfillment"))
                    .UseSnakeCaseNamingConvention());
        }

        if (configuration is not null)
        {
            services.Configure<SourcingOptions>(configuration.GetSection("Fulfillment:Sourcing"));
            services.Configure<OptimizationOptions>(configuration.GetSection("Fulfillment:Optimization"));
        }

        services.AddHttpClient<OsrmRouteProvider>((sp, client) =>
        {
            var baseUrl = configuration?["Fulfillment:Optimization:OsrmBaseUrl"] ?? "http://localhost:5000";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(2);
        });

        services.AddSingleton<HaversineRouteProvider>();
        services.AddSingleton<IRouteProvider>(sp =>
        {
            var osrm = sp.GetRequiredService<OsrmRouteProvider>();
            return new CachingRouteProvider(osrm, "v1");
        });

        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OptimizationOptions>>().Value);
        services.AddSingleton<FulfillmentCostModel>();
        services.AddSingleton<SourcingOptimizer>();

        services.AddScoped<IFulfillmentStore, FulfillmentStore>();
        services.AddScoped<NetworkInventoryView>();
        services.AddScoped<EvaluateSourcing>();
        services.AddScoped<CommitSourcingDecision>();
        services.AddScoped<GetSourcing>();

        return services;
    }
}
