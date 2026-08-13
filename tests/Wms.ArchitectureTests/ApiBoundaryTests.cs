using Xunit;

namespace Wms.ArchitectureTests;

public sealed class ApiBoundaryTests
{
    [Fact]
    public void Modules_do_not_reference_the_api_assembly()
    {
        foreach (var module in ArchitectureCatalog.Modules)
        {
            Assert.DoesNotContain(ArchitectureCatalog.ApiAssemblyName, module.ReferencedAssemblyNames);
        }
    }

    [Fact]
    public void No_module_type_depends_on_any_api_type()
    {
        var violations = ArchitectureCatalog.Modules
            .SelectMany(module => module.Types.Select(type => (module.Name, Type: type)))
            .SelectMany(entry => TypeReferenceScanner
                .GetReferencedNamespaces(entry.Type)
                .Where(ns => ns == "Wms.Api" || ns.StartsWith("Wms.Api.", StringComparison.Ordinal))
                .Select(ns => $"{entry.Name} :: {entry.Type.FullName} -> {ns}"))
            .ToList();

        Assert.False(violations.Count > 0, "Modüller composition root'a (Wms.Api) bağımlı olamaz — DAG kuralı. İhlaller: " + string.Join("; ", violations));
    }
}
