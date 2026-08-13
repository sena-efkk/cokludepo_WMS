using Xunit;

namespace Wms.ArchitectureTests;

public sealed class ContractBoundaryTests
{
    [Fact]
    public void Cross_module_references_are_allowed_only_through_contracts_namespaces()
    {
        var violations = new List<string>();

        foreach (var module in ArchitectureCatalog.Modules)
        {
            foreach (var type in module.Types)
            {
                foreach (var referencedNamespace in TypeReferenceScanner.GetReferencedNamespaces(type))
                {
                    if (!referencedNamespace.StartsWith("Wms.Modules.", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var remainder = referencedNamespace["Wms.Modules.".Length..];
                    var otherModule = remainder.Split('.')[0];
                    if (string.Equals(module.Name, "Wms.Modules." + otherModule, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (remainder == $"{otherModule}.Contracts"
                        || remainder.StartsWith($"{otherModule}.Contracts.", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    violations.Add($"{module.Name} :: {type.FullName} -> {referencedNamespace} (yalnızca .Contracts izinli)");
                }
            }
        }

        Assert.False(
            violations.Count > 0,
            "Çapraz modül erişimleri yalnızca Contracts namespace'leri üzerinden olabilir (ADR-0001). İhlaller: "
            + string.Join("; ", violations));
    }
}
