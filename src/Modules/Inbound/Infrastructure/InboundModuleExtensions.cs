using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wms.Modules.Inbound.Application;
using Wms.Modules.Inbound.Contracts;
using Wms.Modules.Inbound.Infrastructure.Persistence;

namespace Wms.Modules.Inbound.Infrastructure;

public static class InboundModuleExtensions
{
    public static IServiceCollection AddInboundModule(
        this IServiceCollection services,
        string? connectionString,
        IConfiguration? configuration = null)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<InboundDbContext>(options =>
                options
                    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "inbound"))
                    .UseSnakeCaseNamingConvention());
        }

        if (configuration is not null)
        {
            services.Configure<InboundOptions>(configuration.GetSection("Inbound"));
        }

        services.AddScoped<InboundStore>();
        services.AddScoped<IInboundStore>(sp => sp.GetRequiredService<InboundStore>());
        services.AddScoped<Wms.Integration.Outbox.IOutboxStore>(sp => sp.GetRequiredService<InboundStore>());
        services.AddScoped<IInboundContract, InboundContractAdapter>();
        services.AddScoped<CreateReceipt>();
        services.AddScoped<ReceiveItems>();
        services.AddScoped<StartPutaway>();
        services.AddScoped<CompletePutaway>();
        services.AddScoped<CancelReceipt>();
        services.AddScoped<GetReceipt>();
        services.AddScoped<GetInboundSummary>();
        services.AddScoped<ListReceipts>();
        services.AddScoped<GetPutawayTask>();
        services.AddScoped<ListPutawayTasks>();

        return services;
    }
}
