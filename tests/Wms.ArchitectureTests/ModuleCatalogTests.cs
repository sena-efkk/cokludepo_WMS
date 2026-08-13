using Xunit;

namespace Wms.ArchitectureTests;

public sealed class ModuleCatalogTests
{
    [Fact]
    public void Solution_contains_exactly_the_expected_business_modules()
    {
        var actual = Directory
            .GetFiles(AppContext.BaseDirectory, "Wms.Modules.*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = ArchitectureCatalog.ExpectedModuleAssemblyNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Integration_technical_boundary_has_no_project_yet()
    {
        var assemblyNames = Directory
            .GetFiles(AppContext.BaseDirectory, "*.dll")
            .Select(Path.GetFileNameWithoutExtension);

        Assert.DoesNotContain("Wms.Modules.Integration", assemblyNames);
    }
}
