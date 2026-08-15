namespace Wms.ArchitectureTests;

internal static class ArchitectureCatalog
{
    public const string ApiAssemblyName = "Wms.Api";

    public static readonly string[] ExpectedModuleAssemblyNames =
    [
        "Wms.Modules.MasterData",
        "Wms.Modules.Facility",
        "Wms.Modules.Inventory",
        "Wms.Modules.Inbound",
        "Wms.Modules.Outbound",
        "Wms.Modules.Transfers",
        "Wms.Modules.Fulfillment",
        "Wms.Modules.Administration",
    ];

    public static readonly IReadOnlyDictionary<string, string[]> AllowedModuleDependencies =
        new Dictionary<string, string[]>
        {
            ["Wms.Modules.MasterData"] = [],
            ["Wms.Modules.Facility"] = [],
            ["Wms.Modules.Inventory"] = ["Wms.Modules.MasterData", "Wms.Modules.Facility"],
            ["Wms.Modules.Inbound"] = ["Wms.Modules.MasterData", "Wms.Modules.Facility", "Wms.Modules.Inventory"],
            ["Wms.Modules.Outbound"] = ["Wms.Modules.MasterData", "Wms.Modules.Facility", "Wms.Modules.Inventory"],
            ["Wms.Modules.Transfers"] = ["Wms.Modules.MasterData", "Wms.Modules.Facility", "Wms.Modules.Outbound", "Wms.Modules.Inbound"],
            ["Wms.Modules.Fulfillment"] = ["Wms.Modules.MasterData", "Wms.Modules.Facility", "Wms.Modules.Inventory", "Wms.Modules.Transfers", "Wms.Modules.Outbound"],
            ["Wms.Modules.Administration"] = ["Wms.Modules.Facility"],
        };

    public static readonly IReadOnlyList<AssemblyInspector> Modules =
        ExpectedModuleAssemblyNames
            .Select(name => AssemblyInspector.Load(Path.Combine(AppContext.BaseDirectory, name + ".dll")))
            .ToList();

    public static readonly AssemblyInspector Api =
        AssemblyInspector.Load(Path.Combine(AppContext.BaseDirectory, ApiAssemblyName + ".dll"));
}
