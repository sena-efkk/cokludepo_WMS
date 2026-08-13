using System.Text.RegularExpressions;
using Xunit;

namespace Wms.ArchitectureTests;

public sealed class LayerRulesTests
{
    private const string DomainNamespacePattern = @"^Wms\.Modules\.[^.]+\.Domain(\..*)?$";

    [Fact]
    public void Domain_namespaces_do_not_depend_on_any_infrastructure_namespace()
    {
        var violations = FindDomainViolations(@"^Wms\.Modules\.[^.]+\.Infrastructure(\..*)?$");

        Assert.False(
            violations.Count > 0,
            "Domain katmanı Infrastructure'a (kendi modülü dahil) bağımlı olamaz. İhlaller: " + string.Join("; ", violations));
    }

    [Theory]
    [InlineData("^Microsoft\\.AspNetCore")]
    [InlineData("^Microsoft\\.EntityFrameworkCore")]
    [InlineData("^Microsoft\\.Extensions")]
    public void Domain_namespaces_do_not_depend_on_framework_namespaces(string frameworkPattern)
    {
        var violations = FindDomainViolations(frameworkPattern);

        Assert.False(
            violations.Count > 0,
            $"Domain katmanı {frameworkPattern} namespace'ine bağımlı olamaz. İhlaller: " + string.Join("; ", violations));
    }

    private static List<string> FindDomainViolations(string forbiddenNamespacePattern)
    {
        return ArchitectureCatalog.Modules
            .SelectMany(module => module.Types.Select(type => (module.Name, Type: type)))
            .Where(entry => Regex.IsMatch(entry.Type.Namespace, DomainNamespacePattern))
            .SelectMany(entry => TypeReferenceScanner
                .GetReferencedNamespaces(entry.Type)
                .Where(ns => Regex.IsMatch(ns, forbiddenNamespacePattern))
                .Select(ns => $"{entry.Name} :: {entry.Type.FullName} -> {ns}"))
            .ToList();
    }
}
