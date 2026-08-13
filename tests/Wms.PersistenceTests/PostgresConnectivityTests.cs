using Xunit;

namespace Wms.PersistenceTests;

public sealed class PostgresConnectivityTests
{
    private static readonly string[] ExpectedSchemas =
    [
        "master_data",
        "facility",
        "inventory",
        "inbound",
        "outbound",
        "transfers",
        "fulfillment",
        "administration",
    ];

    [Fact]
    public async Task Can_connect_and_execute_a_query()
    {
        var connectionString = ConnectionResolver.Resolve();
        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "ConnectionStrings__WmsDatabase ortam değişkeni veya deploy/.env bulunamadı — PostgreSQL container'ı ayağa kaldırın (docker compose up -d, deploy/).");

        await using var dataSource = Npgsql.NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand("SELECT 1");
        var result = await command.ExecuteScalarAsync();

        Assert.Equal(1, Convert.ToInt32(result));
    }

    [Fact]
    public async Task All_module_schemas_exist()
    {
        var connectionString = ConnectionResolver.Resolve();
        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "ConnectionStrings__WmsDatabase ortam değişkeni veya deploy/.env bulunamadı — PostgreSQL container'ı ayağa kaldırın (docker compose up -d, deploy/).");

        await using var dataSource = Npgsql.NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(
            "SELECT schema_name FROM information_schema.schemata WHERE schema_name = ANY(@names)");
        command.Parameters.AddWithValue("names", ExpectedSchemas);

        var existing = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            existing.Add(reader.GetString(0));
        }

        Assert.Equal(ExpectedSchemas.OrderBy(n => n, StringComparer.Ordinal), existing.OrderBy(n => n, StringComparer.Ordinal));
    }
}
