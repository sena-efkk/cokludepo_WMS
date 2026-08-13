using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Modules.Facility.Application;
using Wms.Modules.Facility.Application.Seed;
using Wms.Modules.Facility.Contracts;
using Wms.Modules.Facility.Infrastructure.Persistence;

namespace Wms.Modules.Facility.Infrastructure;

public static class FacilityModuleExtensions
{
    public static IServiceCollection AddFacilityModule(this IServiceCollection services, string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<FacilityDbContext>(options =>
                options
                    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "facility"))
                    .UseSnakeCaseNamingConvention());
        }

        services.AddScoped<IFacilityStore, FacilityStore>();
        services.AddScoped<IFacilityQueryContract, FacilityQueryContract>();
        services.AddScoped<CreateWarehouse>();
        services.AddScoped<GetWarehouse>();
        services.AddScoped<ListWarehouses>();
        services.AddScoped<DeactivateWarehouse>();
        services.AddScoped<CreateLocation>();
        services.AddScoped<GetLocation>();
        services.AddScoped<ListLocations>();
        services.AddScoped<GetLocationTree>();
        services.AddScoped<DeactivateLocation>();
        services.AddScoped<ReparentLocation>();
        services.AddScoped<SeedDemoFacilities>();

        return services;
    }
}
