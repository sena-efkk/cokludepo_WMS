using Xunit;

namespace Wms.ArchitectureTests;

public sealed class ModuleDependencyTests
{
    [Fact]
    public void Module_assembly_references_respect_the_phase2_dag()
    {
        foreach (var source in ArchitectureCatalog.Modules)
        {
            var allowed = ArchitectureCatalog.AllowedModuleDependencies[source.Name];

            foreach (var targetName in source.ReferencedAssemblyNames)
            {
                if (!targetName.StartsWith("Wms.Modules.", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.True(
                    allowed.Contains(targetName),
                    $"DAG ihlali: {source.Name} -> {targetName} izinli bir kenar değil (docs/architecture/MODULE_MAP.md).");
            }
        }
    }

    [Fact]
    public void Allowed_dag_edges_reference_only_known_modules_and_contain_no_cycle()
    {
        var knownModules = ArchitectureCatalog.ExpectedModuleAssemblyNames.ToHashSet();

        foreach (var (source, targets) in ArchitectureCatalog.AllowedModuleDependencies)
        {
            Assert.True(knownModules.Contains(source), $"Bilinmeyen modül: {source}");

            foreach (var target in targets)
            {
                Assert.True(knownModules.Contains(target), $"{source} bilinmeyen bir modüle kenar içeriyor: {target}");
                Assert.NotEqual(source, target);
            }
        }

        AssertNoCycle();
    }

    private static void AssertNoCycle()
    {
        var visited = new HashSet<string>();
        var inProgress = new HashSet<string>();

        void Visit(string node)
        {
            if (!inProgress.Add(node))
            {
                Assert.Fail($"İzinli DAG kenarlarında cycle tespit edildi: {node}");
            }

            if (visited.Add(node))
            {
                foreach (var target in ArchitectureCatalog.AllowedModuleDependencies[node])
                {
                    Visit(target);
                }
            }

            inProgress.Remove(node);
        }

        foreach (var module in ArchitectureCatalog.ExpectedModuleAssemblyNames)
        {
            Visit(module);
        }
    }
}
