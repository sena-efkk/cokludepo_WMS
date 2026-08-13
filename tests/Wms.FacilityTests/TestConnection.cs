namespace Wms.FacilityTests;

internal static class TestConnection
{
    public static string ResolveOrFail()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("ConnectionStrings__WmsDatabase");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var envFile = FindRepoFile("deploy", ".env");
        if (envFile is not null)
        {
            var variables = File.ReadAllLines(envFile)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1]);

            if (variables.TryGetValue("POSTGRES_DB", out var db)
                && variables.TryGetValue("POSTGRES_USER", out var user)
                && variables.TryGetValue("POSTGRES_PASSWORD", out var password))
            {
                return $"Host=localhost;Port=5432;Database={db};Username={user};Password={password}";
            }
        }

        throw new InvalidOperationException(
            "ConnectionStrings__WmsDatabase ortam değişkeni veya deploy/.env bulunamadı — " +
            "PostgreSQL container'ını ayağa kaldırın (deploy/ içinde docker compose up -d).");
    }

    private static string? FindRepoFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Wms.sln")))
            {
                var path = Path.Combine([directory.FullName, .. relativeParts]);
                return File.Exists(path) ? path : null;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
