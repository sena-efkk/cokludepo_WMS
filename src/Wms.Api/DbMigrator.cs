using Microsoft.EntityFrameworkCore;
using Wms.Modules.Facility.Infrastructure.Persistence;
using Wms.Modules.Fulfillment.Infrastructure.Persistence;
using Wms.Modules.Inbound.Infrastructure.Persistence;
using Wms.Modules.Inventory.Infrastructure.Persistence;
using Wms.Modules.MasterData.Infrastructure.Persistence;
using Wms.Modules.Outbound.Infrastructure.Persistence;
using Wms.Modules.Transfers.Infrastructure.Persistence;

namespace Wms.Api;

public static class DbMigrator
{
    public static void Apply(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        var contexts = new (string Schema, Func<DbContext?> Resolve)[]
        {
            ("master_data", () => provider.GetService<MasterDataDbContext>()),
            ("facility", () => provider.GetService<FacilityDbContext>()),
            ("inventory", () => provider.GetService<InventoryDbContext>()),
            ("inbound", () => provider.GetService<InboundDbContext>()),
            ("outbound", () => provider.GetService<OutboundDbContext>()),
            ("transfers", () => provider.GetService<TransfersDbContext>()),
            ("fulfillment", () => provider.GetService<FulfillmentDbContext>()),
        };

        foreach (var (schema, resolve) in contexts)
        {
            using var context = resolve();
            if (context is null)
            {
                continue;
            }

            logger.LogInformation("Migrating {Schema} schema...", schema);
            context.Database.Migrate();
            logger.LogInformation("Migrating {Schema} schema completed.", schema);
        }
    }
}
