using Aether.Modding;
using Mono.Cecil;
using Mono.Cecil.Cil;
using static Aether.Bootstrap.Selectors;

namespace Aether.Bootstrap;

internal sealed partial class AssemblyWeaver(ModuleDefinition game, bool debug)
{
    private sealed record Declaration(ModPackage Package, Selectors Selectors, TypeDefinition Source, TypeDefinition Target, int Priority, int ModOrder)
    {
        internal string Name => $"mod '{Package.Manifest.Id}' in '{Package.Archive}', mixin '{Source.FullName}'";
    }

    private sealed record Injection(Declaration Declaration, MethodDefinition Handler, MethodDefinition Target,
        CustomAttribute Attribute, int Order, int Sequence);

    private readonly Dictionary<TypeDefinition, Declaration> mixinTypes = new();
    private readonly Dictionary<MethodDefinition, MethodDefinition> methods = new();
    private readonly Dictionary<FieldDefinition, FieldDefinition> fields = new();
    private readonly Dictionary<MethodDefinition, Declaration> overwrites = new();
    private readonly List<Injection> injections = new();

    private readonly Dictionary<string, HashSet<ModPackage>> referenceOwners = new(StringComparer.OrdinalIgnoreCase);
    private ModPackage? importingPackage;

    internal static Dictionary<string, HashSet<ModPackage>> Write(string path, IReadOnlyList<ModPackage> packages, Stream output, bool debug)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        var modules = new List<ModuleDefinition>();
        try
        {
            using var game = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
            var weaver = new AssemblyWeaver(game, debug);
            var originalReferences = game.AssemblyReferences.Select(item => item.FullName).ToHashSet(StringComparer.Ordinal);
            for (int index = 0; index < packages.Count; index++)
            {
                ModPackage package = packages[index];
                try
                {
                    var names = package.Manifest.Mixins ?? [];
                    if (names.Length == 0)
                        continue;

                    var module = ModuleDefinition.ReadModule(package.EntryPath, new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
                    modules.Add(module);
                    var selectors = new Selectors(package.Manifest.Mappings is { } mappingsPath ? Path.Combine(package.Root, mappingsPath) : null);
                    foreach (string name in names)
                    {
                        var source = module.GetType(name.Replace('+', '/')) ?? throw new InvalidDataException($"Unknown mixin type '{name}'.");
                        var attribute = Attribute(source, "Mixin") ?? throw new InvalidDataException($"'{name}' has no Mixin attribute.");
                        var authored = attribute.ConstructorArguments[0].Value;
                        string selector = authored is TypeReference type ? type.FullName : (string)authored;
                        var target = game.GetType(selectors.Map(selector).Replace('+', '/'))
                            ?? throw new InvalidDataException($"Unknown target type {selectors.Describe(selector)}.");
                        if (source.HasGenericParameters || target.HasGenericParameters || target.IsValueType || target.IsInterface)
                            throw new InvalidDataException($"Unsupported mixin type or target '{target.FullName}'.");
                        var declaration = new Declaration(package, selectors, source, target, Option(attribute, "Priority", 1000), index);
                        if (!weaver.mixinTypes.TryAdd(source, declaration))
                            throw new InvalidDataException($"Duplicate mixin '{name}'.");
                    }
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException($"Mod '{package.Manifest.Id}' in '{package.Archive}': {exception.Message}", exception);
                }
            }

            var declarations = weaver.mixinTypes.Values.OrderByDescending(item => item.Priority)
                .ThenBy(item => item.ModOrder).ThenBy(item => item.Source.FullName, StringComparer.Ordinal).ToArray();
            foreach (Declaration declaration in declarations)
                weaver.WithContext(declaration, () => weaver.DeclareMembers(declaration));
            foreach (Declaration declaration in declarations)
                weaver.WithContext(declaration, () => weaver.CopyMembers(declaration));

            weaver.ApplyInjections();
            game.Write(output);
            return weaver.referenceOwners.Where(reference => !originalReferences.Contains(reference.Key))
                .ToDictionary(reference => reference.Key, reference => reference.Value, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (ModuleDefinition module in modules)
                module.Dispose();
        }
    }

    private void WithContext(Declaration declaration, Action action)
    {
        ModPackage? previous = importingPackage;
        importingPackage = declaration.Package;
        try { action(); }
        catch (Exception exception)
        {
            throw new InvalidDataException($"{declaration.Name} targeting '{declaration.Target.FullName}': {exception.Message}", exception);
        }
        finally { importingPackage = previous; }
    }

    private void DeclareMembers(Declaration declaration)
    {
        var (package, selectors, source, target, _, _) = declaration;
        if (source.IsInterface)
            return;

        if (source.HasProperties || source.HasEvents || source.HasNestedTypes)
            throw new InvalidDataException("Mixin classes must declare fields and methods directly.");

        foreach (FieldDefinition field in source.Fields)
        {
            TypeReference type = ImportType(field.FieldType);
            if (Attribute(field, "Shadow") is { } shadow)
            {
                string selector = Text(shadow, 0) ?? field.Name;
                fields.Add(field, target.Fields.SingleOrDefault(item => MemberMatches(item, selectors.Map(selector)) &&
                    Same(item.FieldType, type) && item.IsStatic == field.IsStatic)
                    ?? throw new InvalidDataException($"Cannot bind shadow field {selectors.Describe(selector)}."));
                continue;
            }

            if (field.HasLayoutInfo || field.HasMarshalInfo || field.RVA != 0 || field.IsInitOnly)
                throw new InvalidDataException($"Unsupported merged field '{field.Name}'.");

            string name = field.Name;
            if (target.Fields.Any(item => item.Name == name))
            {
                if (Attribute(field, "Unique") == null)
                    throw new InvalidDataException($"Field collision for '{name}'.");
                name = UniqueName(declaration, name);
            }
            if (target.Fields.Any(item => item.Name == name))
                throw new InvalidDataException($"Field collision for generated name '{name}'.");

            var copy = new FieldDefinition(name, field.Attributes, type);
            if (field.HasConstant)
                copy.Constant = field.Constant;
            fields.Add(field, copy);
            target.Fields.Add(copy);
        }

        foreach (MethodDefinition method in source.Methods)
        {
            if (method.IsConstructor)
            {
                if (!IsEmptyMixinConstructor(method))
                    throw new InvalidDataException("Initialize merged fields using an injection into the target constructor.");
                continue;
            }
            if (method.HasGenericParameters || method.IsPInvokeImpl || (method.IsVirtual && !method.IsFinal))
                throw new InvalidDataException($"Unsupported mixin method '{method.FullName}'.");

            CustomAttribute? shadow = Attribute(method, "Shadow");
            CustomAttribute? overwrite = Attribute(method, "Overwrite");
            if (shadow != null && overwrite != null)
                throw new InvalidDataException($"'{method.Name}' cannot be both a shadow and an overwrite.");
            if (shadow != null || overwrite != null)
            {
                string selector = Text(shadow ?? overwrite!, 0) ?? method.Name;
                var existing = target.Methods.SingleOrDefault(item => MemberMatches(item, selectors.Map(selector)) && Matches(method, item))
                    ?? throw new InvalidDataException($"Cannot bind '{method.Name}' to {selectors.Describe(selector)}.");
                if (overwrite != null && (existing.IsConstructor || !existing.HasBody || !overwrites.TryAdd(existing, declaration)))
                    throw new InvalidDataException($"Invalid or duplicate overwrite of '{existing.FullName}'.");
                methods.Add(method, existing);
                continue;
            }

            if (!method.HasBody)
                throw new InvalidDataException($"Missing body for '{method.FullName}'.");

            bool handler = InjectorAttributes(method).Any();
            string name = handler ? UniqueName(declaration, method.Name) : method.Name;
            if (!handler && target.Methods.Any(item => item.Name == name && SameParameters(method, item)))
            {
                if (Attribute(method, "Unique") == null)
                    throw new InvalidDataException($"Method collision for '{method.FullName}'.");
                name = UniqueName(declaration, method.Name);
            }
            if (target.Methods.Any(item => item.Name == name && SameParameters(method, item)))
                throw new InvalidDataException($"Method collision for generated name '{name}'.");

            MethodAttributes flags = method.Attributes;
            if (handler)
                flags = (flags & ~MethodAttributes.MemberAccessMask) | MethodAttributes.Private;
            var copy = new MethodDefinition(name, flags, ImportType(method.ReturnType));
            foreach (ParameterDefinition parameter in method.Parameters)
                copy.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, ImportType(parameter.ParameterType)));
            methods.Add(method, copy);
            target.Methods.Add(copy);
        }

        foreach (InterfaceImplementation implementation in source.Interfaces)
        {
            TypeReference type = ImportType(implementation.InterfaceType);
            if (!target.Interfaces.Any(item => Same(item.InterfaceType, type)))
                target.Interfaces.Add(new InterfaceImplementation(type));
        }
    }

    private bool SameParameters(MethodReference source, MethodReference target) =>
        source.Parameters.Select(item => ImportType(item.ParameterType).FullName)
            .SequenceEqual(target.Parameters.Select(item => item.ParameterType.FullName), StringComparer.Ordinal);

    private bool Matches(MethodDefinition source, MethodDefinition target) =>
        target.IsStatic == source.IsStatic && !target.HasGenericParameters
        && Same(ImportType(source.ReturnType), target.ReturnType) && SameParameters(source, target);

    private static bool Same(TypeReference first, TypeReference second) => first.FullName == second.FullName;

    private static string UniqueName(Declaration declaration, string name) =>
        $"__aether${declaration.Package.Manifest.Id}${declaration.Source.FullName}${name}";

    private static bool IsEmptyMixinConstructor(MethodDefinition method)
    {
        if (method.IsStatic || !method.HasBody)
            return false;
        var code = method.Body.Instructions.Where(item => item.OpCode.Code != Code.Nop).ToArray();
        return code.Length == 3 && code[0].OpCode.Code == Code.Ldarg_0 && code[1].OpCode.Code == Code.Call
            && code[1].Operand is MethodReference called && called.Name == ".ctor"
            && called.DeclaringType.FullName == "System.Object" && code[2].OpCode.Code == Code.Ret;
    }

    private static IEnumerable<CustomAttribute> InjectorAttributes(MethodDefinition method) =>
        method.CustomAttributes.Where(item => item.AttributeType.FullName is
            "Aether.Modding.InjectAttribute" or "Aether.Modding.RedirectAttribute" or
            "Aether.Modding.ModifyArgAttribute" or "Aether.Modding.ModifyConstantAttribute");

    private void CopyMembers(Declaration declaration)
    {
        if (declaration.Source.IsInterface)
        {
            AddAccessors(declaration);
            return;
        }

        foreach (MethodDefinition source in declaration.Source.Methods.Where(method => !method.IsConstructor))
        {
            if (Attribute(source, "Shadow") != null)
                continue;
            MethodDefinition handler = methods[source];
            CloneBody(source, handler);
            foreach (MethodReference implementation in source.Overrides)
                handler.Overrides.Add(ImportMethod(implementation));
            foreach (CustomAttribute attribute in InjectorAttributes(source))
            {
                string selector = Text(attribute, 0)!;
                MethodDefinition? target = declaration.Selectors.Method(declaration.Target, selector);
                if (target == null && Option(attribute, "Require", 1) == 0 &&
                    (!debug || Option(attribute, "Expect", 1) == 0))
                    continue;
                if (target == null || !target.HasBody || target.HasGenericParameters || target.ReturnType.IsByReference)
                    throw new InvalidDataException($"Unsupported or missing injection target {declaration.Selectors.Describe(selector)}.");
                if (overwrites.ContainsKey(target))
                    throw new InvalidDataException($"Cannot inject into overwritten method '{target.FullName}'.");
                int order = Option(attribute, "Order", attribute.AttributeType.Name == "RedirectAttribute" ? 10000 : 1000);
                injections.Add(new Injection(declaration, handler, target, attribute, order, injections.Count));
            }
        }
    }

}
