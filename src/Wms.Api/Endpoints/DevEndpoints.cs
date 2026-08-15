using Microsoft.AspNetCore.Mvc;
using Wms.Api;

namespace Wms.Api.Endpoints;

public static class DevEndpoints
{
    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        var group = endpoints.MapGroup("/api/dev");

        group.MapPost("/scenarios/{scenario}/initialize", async (string scenario, [FromServices] ScenarioInitializer initializer, CancellationToken ct) =>
        {
            if (!configuration.GetValue<bool>("DevFeatures:Enabled"))
            {
                return Results.NotFound();
            }

            var result = await initializer.InitializeAsync(scenario, ct);
            return Results.Ok(result);
        });

        return endpoints;
    }
}
