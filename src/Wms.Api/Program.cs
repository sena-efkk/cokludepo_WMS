using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wms.Api.Endpoints;
using Wms.Modules.Facility.Infrastructure;
using Wms.Modules.Inventory.Infrastructure;
using Wms.Modules.MasterData.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("WmsDatabase");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddSingleton(Npgsql.NpgsqlDataSource.Create(connectionString));
}

builder.Services.AddMasterDataModule(connectionString);
builder.Services.AddFacilityModule(connectionString);
builder.Services.AddInventoryModule(connectionString, builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck("self", _ => HealthCheckResult.Healthy("Wms.Api"), tags: ["app"])
    .AddCheck<Wms.Api.PostgresHealthCheck>("postgresql", tags: ["db"]);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "Wms.Api", status = "running" }));

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = Wms.Api.HealthResponseWriter.WriteAsync,
});

app.MapMasterDataEndpoints();
app.MapFacilityEndpoints();
app.MapInventoryEndpoints();

app.Run();

public partial class Program;
