using Wms.Modules.Facility.Domain;

namespace Wms.Modules.Facility.Application.Seed;

public sealed class SeedDemoFacilities(IFacilityStore store)
{
    public async Task<FacilitySeedResult> Handle(IReadOnlyList<FacilitySeedPlan> plans, CancellationToken cancellationToken)
    {
        var warehousesCreated = 0;
        var locationsCreated = 0;
        var skipped = 0;

        foreach (var plan in plans)
        {
            var warehouse = await store.GetWarehouseByCodeAsync(plan.WarehouseCode, cancellationToken);
            if (warehouse is null)
            {
                warehouse = Warehouse.Create(
                    plan.WarehouseCode,
                    plan.WarehouseName,
                    city: plan.City,
                    countryCode: "TR",
                    latitude: plan.Latitude,
                    longitude: plan.Longitude);
                await store.AddWarehouseAsync(warehouse, cancellationToken);
                warehousesCreated++;
            }

            var byCode = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in await store.ListLocationsAsync(warehouse.Id, includeInactive: true, cancellationToken))
            {
                byCode[existing.Code] = existing.Id;
            }

            foreach (var item in plan.Locations)
            {
                if (byCode.ContainsKey(item.Code))
                {
                    skipped++;
                    continue;
                }

                Guid? parentId = null;
                if (item.ParentCode is not null)
                {
                    if (!byCode.TryGetValue(item.ParentCode, out var parent))
                    {
                        throw new InvalidOperationException(
                            $"Seed sırası hatalı: {item.Code} için parent {item.ParentCode} henüz oluşturulmadı.");
                    }

                    parentId = parent;
                }

                var location = Location.Create(
                    warehouse.Id,
                    parentId,
                    item.Code,
                    item.Name,
                    item.Type,
                    item.AllowsPicking,
                    item.AllowsPutaway,
                    item.AllowsReplenishment,
                    item.HoldsInventory);

                await store.AddLocationAsync(location, cancellationToken);
                byCode[location.Code] = location.Id;
                locationsCreated++;
            }
        }

        await store.SaveChangesAsync(cancellationToken);
        return new FacilitySeedResult(warehousesCreated, locationsCreated, skipped);
    }
}
