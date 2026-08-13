using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Modules.MasterData.Application;
using Wms.Modules.MasterData.Application.Import;
using Wms.Modules.MasterData.Contracts;
using Wms.Modules.MasterData.Infrastructure.Persistence;

namespace Wms.Modules.MasterData.Infrastructure;

public static class MasterDataModuleExtensions
{
    public static IServiceCollection AddMasterDataModule(this IServiceCollection services, string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<MasterDataDbContext>(options =>
                options
                    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "master_data"))
                    .UseSnakeCaseNamingConvention());
        }

        services.AddScoped<IMasterDataStore, MasterDataStore>();
        services.AddScoped<IMasterDataQueryContract, MasterDataQueryContract>();
        services.AddScoped<CreateProduct>();
        services.AddScoped<GetProduct>();
        services.AddScoped<ListProducts>();
        services.AddScoped<CreateSku>();
        services.AddScoped<GetSku>();
        services.AddScoped<ListSkus>();
        services.AddScoped<DeactivateSku>();
        services.AddScoped<ImportCatalog>();

        return services;
    }
}
