using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Wms.Api.Endpoints;
using Wms.Integration.Messaging;
using Wms.Integration.Outbox;
using Wms.Modules.Facility.Infrastructure;
using Wms.Modules.Fulfillment.Infrastructure;
using Wms.Modules.Inbound.Infrastructure;
using Wms.Modules.Inventory.Infrastructure;
using Wms.Modules.MasterData.Infrastructure;
using Wms.Modules.Outbound.Infrastructure;
using Wms.Modules.Transfers.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("WmsDatabase");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddSingleton(Npgsql.NpgsqlDataSource.Create(connectionString));
}

builder.Services.AddMasterDataModule(connectionString);
builder.Services.AddFacilityModule(connectionString);
builder.Services.AddInventoryModule(connectionString, builder.Configuration);
builder.Services.AddInboundModule(connectionString, builder.Configuration);
builder.Services.AddOutboundModule(connectionString, builder.Configuration);
builder.Services.AddTransfersModule(connectionString, builder.Configuration);
builder.Services.AddFulfillmentModule(connectionString, builder.Configuration);
builder.Services.AddScoped<Wms.Api.ScenarioInitializer>();

// Integration: transactional outbox + RabbitMQ + idempotent consumers.
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<IRabbitMqPublisher>(sp => sp.GetRequiredService<RabbitMqPublisher>());
builder.Services.AddSingleton<OutboxDispatcher>();
builder.Services.AddHostedService<OutboxDispatcherService>();
builder.Services.AddHostedService<IntegrationConsumerService>();
builder.Services.AddHostedService<OutboxRetentionService>();
builder.Services.Configure<OutboxRetentionOptions>(builder.Configuration.GetSection("Outbox"));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("wms-api"))
    .WithMetrics(metrics => metrics
        .AddHttpClientInstrumentation()
        .AddMeter("Wms.Metrics")
        .AddPrometheusExporter());

builder.Services.AddHealthChecks()
    .AddCheck("self", _ => HealthCheckResult.Healthy("Wms.Api"), tags: ["app"])
    .AddCheck<Wms.Api.PostgresHealthCheck>("postgresql", tags: ["db"])
    .AddCheck<Wms.Api.RabbitMqHealthCheck>("rabbitmq", tags: ["infra"])
    .AddCheck<Wms.Api.OsrmHealthCheck>("osrm", tags: ["optional"]);

var app = builder.Build();

Wms.Api.DbMigrator.Apply(app.Services, app.Logger);

app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.MapGet("/", () => Results.Ok(new { service = "Wms.Api", status = "running" }));

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = Wms.Api.HealthResponseWriter.WriteAsync,
});

app.MapMasterDataEndpoints();
app.MapFacilityEndpoints();
app.MapInventoryEndpoints();
app.MapInboundEndpoints();
app.MapOutboundEndpoints();
app.MapTransferEndpoints();
app.MapNetworkInventoryEndpoints();
app.MapFulfillmentEndpoints();
app.MapDevEndpoints(builder.Configuration);

app.Run();

public partial class Program;
