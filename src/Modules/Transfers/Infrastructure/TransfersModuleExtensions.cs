using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wms.Integration.Messaging;
using Wms.Modules.Transfers.Application;
using Wms.Modules.Transfers.Contracts;
using Wms.Modules.Transfers.Infrastructure.Persistence;

namespace Wms.Modules.Transfers.Infrastructure;

public static class TransfersModuleExtensions
{
    public static IServiceCollection AddTransfersModule(
        this IServiceCollection services,
        string? connectionString,
        IConfiguration? configuration = null)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<TransfersDbContext>(options =>
                options
                    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "transfers"))
                    .UseSnakeCaseNamingConvention());
        }

        services.AddScoped<ITransferStore, TransferStore>();
        services.AddScoped<ITransferContract, TransferContractAdapter>();
        services.AddScoped<CreateTransfer>();
        services.AddScoped<AllocateTransfer>();
        services.AddScoped<ShipTransfer>();
        services.AddScoped<ReceiveTransfer>();
        services.AddScoped<ConfirmTransferVariance>();
        services.AddScoped<CancelTransfer>();
        services.AddScoped<GetTransfer>();
        services.AddScoped<GetTransfersSummary>();
        services.AddScoped<ListTransfers>();

        services.AddScoped<TransferEventConsumer>();
        services.AddScoped<IIntegrationConsumer>(sp => sp.GetRequiredService<TransferEventConsumer>());

        return services;
    }
}

public sealed class TransferContractAdapter(ITransferStore store) : ITransferContract
{
    public Task<int> GetOpenInTransitTotalAsync(CancellationToken cancellationToken) =>
        store.GetOpenInTransitTotalAsync(cancellationToken);

    public Task<int> GetOpenInTransitBySkuAsync(Guid skuId, CancellationToken cancellationToken) =>
        store.GetOpenInTransitBySkuAsync(skuId, cancellationToken);
}
