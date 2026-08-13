using Mono.Cecil;

namespace Wms.ArchitectureTests;

internal sealed class AssemblyInspector
{
    private AssemblyInspector(
        AssemblyDefinition definition,
        IReadOnlyList<string> referencedAssemblyNames,
        IReadOnlyList<TypeDefinition> types)
    {
        Definition = definition;
        ReferencedAssemblyNames = referencedAssemblyNames;
        Types = types;
    }

    public AssemblyDefinition Definition { get; }

    public string Name => Definition.Name.Name;

    public IReadOnlyList<string> ReferencedAssemblyNames { get; }

    public IReadOnlyList<TypeDefinition> Types { get; }

    public static AssemblyInspector Load(string dllPath)
    {
        var definition = AssemblyDefinition.ReadAssembly(dllPath);
        var referencedAssemblyNames = definition.MainModule.AssemblyReferences
            .Select(reference => reference.Name)
            .ToList();
        var types = definition.MainModule.Types.ToList();
        return new AssemblyInspector(definition, referencedAssemblyNames, types);
    }
}

internal static class TypeReferenceScanner
{
    public static HashSet<string> GetReferencedNamespaces(TypeDefinition type)
    {
        var namespaces = new HashSet<string>();

        void Add(TypeReference? reference)
        {
            if (reference is null)
            {
                return;
            }

            var element = reference is GenericInstanceType generic ? generic.ElementType : reference;
            var ns = element.Namespace;
            if (!string.IsNullOrEmpty(ns))
            {
                namespaces.Add(ns);
            }
        }

        if (type.BaseType is not null)
        {
            Add(type.BaseType);
        }

        foreach (var iface in type.Interfaces)
        {
            Add(iface.InterfaceType);
        }

        foreach (var field in type.Fields)
        {
            Add(field.FieldType);
        }

        foreach (var method in type.Methods)
        {
            Add(method.ReturnType);

            foreach (var parameter in method.Parameters)
            {
                Add(parameter.ParameterType);
            }

            foreach (var genericParameter in method.GenericParameters)
            {
                foreach (var constraint in genericParameter.Constraints)
                {
                    Add(constraint.ConstraintType);
                }
            }

            if (!method.HasBody)
            {
                continue;
            }

            foreach (var variable in method.Body.Variables)
            {
                Add(variable.VariableType);
            }

            foreach (var instruction in method.Body.Instructions)
            {
                switch (instruction.Operand)
                {
                    case TypeReference typeReference:
                        Add(typeReference);
                        break;
                    case MethodReference methodReference:
                        Add(methodReference.DeclaringType);
                        break;
                    case FieldReference fieldReference:
                        Add(fieldReference.DeclaringType);
                        break;
                }
            }
        }

        foreach (var nested in type.NestedTypes)
        {
            foreach (var ns in GetReferencedNamespaces(nested))
            {
                namespaces.Add(ns);
            }
        }

        return namespaces;
    }
}
