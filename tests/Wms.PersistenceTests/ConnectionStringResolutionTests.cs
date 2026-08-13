using Microsoft.Extensions.Configuration;
using Xunit;

namespace Wms.PersistenceTests;

public sealed class ConnectionStringResolutionTests
{
    [Fact]
    public void Environment_variable_overrides_appsettings_value()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WmsDatabase"] = "Host=env-host;Database=env-db",
            })
            .Build();

        Assert.Equal("Host=env-host;Database=env-db", configuration.GetConnectionString("WmsDatabase"));
    }

    [Fact]
    public void Without_override_connection_string_resolves_to_empty()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .Build();

        Assert.Equal(string.Empty, configuration.GetConnectionString("WmsDatabase"));
    }
}
