namespace Wms.Integration.Messaging;

public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string Username { get; set; } = "wms";

    public string Password { get; set; } = "wms-dev-password";

    public string VirtualHost { get; set; } = "/";

    public string Exchange { get; set; } = "wms-integration";

    public string DeadLetterExchange { get; set; } = "wms-integration-dlx";

    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
