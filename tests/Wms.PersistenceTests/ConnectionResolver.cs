namespace Wms.PersistenceTests;

internal static class ConnectionResolver
{
    public static string? Resolve()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("ConnectionStrings__WmsDatabase");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var envFile = FindRepoFile("deploy", ".env");
        if (envFile is null)
        {
            return null;
        }

        var variables = File.ReadAllLines(envFile)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);

        if (!variables.TryGetValue("POSTGRES_DB", out var db)
            || !variables.TryGetValue("POSTGRES_USER", out var user)
            || !variables.TryGetValue("POSTGRES_PASSWORD", out var password))
        {
            return null;
        }

        return $"Host=localhost;Port=5432;Database={db};Username={user};Password={password}";
    }

    public static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Wms.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Wms.sln bulunamadı — repo root tespit edilemedi.");
    }

    private static string? FindRepoFile(params string[] relativeParts)
    {
        var path = Path.Combine([FindRepoRoot(), .. relativeParts]);
        return File.Exists(path) ? path : null;
    }
}
