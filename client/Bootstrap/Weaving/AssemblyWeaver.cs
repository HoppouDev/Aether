using Aether.Modding;
using Mono.Cecil;
using Mono.Cecil.Cil;
using static Aether.Bootstrap.Selectors;

using static Aether.Bootstrap.TypeSignatures;
namespace Aether.Bootstrap;

internal sealed partial class AssemblyWeaver(ModuleDefinition game, bool debug)
{
    private sealed record Declaration(ModPackage Package, Selectors Selectors, TypeDefinition Source, TypeDefinition Target, int Priority, int ModOrder)
    {
        internal string Name => $"mod '{Package.Manifest.Id}' in '{Package.Archive}', mixin '{Source.FullName}'";
    }

    private sealed record Injection(Declaration Declaration, MethodDefinition Handler, MethodDefinition Target,
        CustomAttribute Attribute, int Order, int Sequence, MethodDefinition Source);

    private readonly Dictionary<TypeDefinition, Declaration> mixinTypes = new();
    private readonly Dictionary<MethodDefinition, MethodDefinition> methods = new();
    private readonly Dictionary<FieldDefinition, FieldDefinition> fields = new();
    private readonly Dictionary<MethodDefinition, Declaration> overwrites = new();
    private readonly Dictionary<TypeDefinition, MethodDefinition[]> originalMethods = new();
    private readonly Dictionary<TypeDefinition, FieldDefinition[]> originalFields = new();
    private readonly List<Injection> injections = new();
    private readonly List<Injection> methodWrappers = new();
    private readonly List<Injection> ilEditors = new();

    private readonly Dictionary<string, HashSet<ModPackage>> referenceOwners = new(StringComparer.OrdinalIgnoreCase);
    private ModPackage? importingPackage;

    internal static Dictionary<string, HashSet<ModPackage>> Write(string path, IReadOnlyList<ModPackage> packages, Stream output, bool debug)
    {
        GameAssemblyResolver CreateResolver(ModuleDefinition? game = null, string? modDirectory = null)
        {
            var resolver = new GameAssemblyResolver { Game = game };
            resolver.RemoveSearchDirectory(".");
            resolver.RemoveSearchDirectory("bin");
            if (modDirectory != null)
                resolver.AddSearchDirectory(modDirectory);
            resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
            resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
            return resolver;
        }

        using var gameResolver = CreateResolver();
        var modules = new List<(ModuleDefinition Module, DefaultAssemblyResolver Resolver)>();
        try
        {
            using var game = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = gameResolver, InMemory = true });
            gameResolver.Game = game;
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

                    var resolver = CreateResolver(game, Path.GetDirectoryName(package.EntryPath));
                    ModuleDefinition module;
                    try { module = ModuleDefinition.ReadModule(package.EntryPath, new ReaderParameters { AssemblyResolver = resolver, InMemory = true }); }
                    catch { resolver.Dispose(); throw; }
                    modules.Add((module, resolver));
                    var selectors = new Selectors(package.Manifest.Mappings is { } mappingsPath ? Path.Combine(package.Root, mappingsPath) : null);
                    foreach (string name in names)
                    {
                        var source = module.GetType(name.Replace('+', '/')) ?? throw new InvalidDataException($"Unknown mixin type '{name}'.");
                        var attribute = Attribute(source, "Mixin") ?? throw new InvalidDataException($"'{name}' has no Mixin attribute.");
                        var authored = attribute.ConstructorArguments[0].Value;
                        string selector = authored is TypeReference type ? type.FullName : (string)authored;
                        var target = game.GetType(selectors.Map(selector).Replace('+', '/'))
                            ?? throw new InvalidDataException($"Unknown target type {selectors.Describe(selector)}.");
                        if (source.HasGenericParameters || !IsEditorMixin(source) && (target.HasGenericParameters || target.IsValueType || target.IsInterface))
                            throw new InvalidDataException($"Unsupported mixin type or target '{target.FullName}'.");
                        var declaration = new Declaration(package, selectors, source, target, Option(attribute, "Priority", 1000), index);
                        if (!weaver.originalMethods.ContainsKey(target))
                        {
                            weaver.originalMethods.Add(target, target.Methods.ToArray());
                            weaver.originalFields.Add(target, target.Fields.ToArray());
                        }
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
            var log = AetherLog.For("Aether.Mixins");
            var started = System.Diagnostics.Stopwatch.StartNew();
            foreach (Declaration declaration in declarations)
                log.Debug("{Mod}: {Mixin} -> {Target} (priority {Priority})", declaration.Package.Manifest.Id,
                    declaration.Source.FullName, declaration.Target.FullName, declaration.Priority);
            foreach (Declaration declaration in declarations)
                weaver.WithContext(declaration, () => weaver.DeclareMembers(declaration));
            foreach (Declaration declaration in declarations)
                weaver.WithContext(declaration, () => weaver.CopyMembers(declaration));

            weaver.ApplyInjections();
            weaver.ApplyILEditors();
            weaver.ApplyMethodWrappers();
            game.Write(output);
            log.Information("Applied {Mixins} mixins from {Mods} mods to {Targets} types in {Elapsed} ms", declarations.Length,
                declarations.Select(item => item.Package).Distinct().Count(), declarations.Select(item => item.Target).Distinct().Count(),
                started.ElapsedMilliseconds);
            return weaver.referenceOwners.Where(reference => !originalReferences.Contains(reference.Key))
                .ToDictionary(reference => reference.Key, reference => reference.Value, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var (module, resolver) in modules)
            {
                module.Dispose();
                resolver.Dispose();
            }
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
        if (IsEditorMixin(source))
        {
            if (source.IsInterface || source.Methods.Any(method => InjectorAttributes(method).Any() ||
                Attribute(method, "Shadow") != null || Attribute(method, "Overwrite") != null || Attribute(method, "Unique") != null) ||
                source.Fields.Any(field => Attribute(field, "Shadow") != null || Attribute(field, "Unique") != null))
                throw new InvalidDataException("Declare startup IL editors in a separate mixin class.");
            foreach (MethodDefinition method in source.Methods.Where(method => method.CustomAttributes.Any(IsILEditor)))
                if (!method.IsStatic || !method.HasBody || method.HasGenericParameters || method.IsConstructor ||
                    method.CallingConvention == MethodCallingConvention.VarArg ||
                    method.ReturnType.MetadataType != MetadataType.Void || method.Parameters.Count != 1 ||
                    method.Parameters[0].ParameterType.FullName != typeof(ILPatchContext).FullName)
                    throw new InvalidDataException($"IL editor '{method.FullName}' must be a static void method taking ILPatchContext.");
            return;
        }
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
                fields.Add(field, originalFields[target].SingleOrDefault(item => MemberMatches(item, selectors.Map(selector)) &&
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
            bool wrapper = InjectorAttributes(method).Any(attribute => attribute.AttributeType.Name is "WrapMethodAttribute" or "WrapOperationAttribute");
            if (method.HasGenericParameters && !wrapper || wrapper && method.CallingConvention == MethodCallingConvention.VarArg ||
                method.IsPInvokeImpl || method.IsVirtual && !method.IsFinal)
                throw new InvalidDataException($"Unsupported mixin method '{method.FullName}'.");

            CustomAttribute? shadow = Attribute(method, "Shadow");
            CustomAttribute? overwrite = Attribute(method, "Overwrite");
            if (shadow != null && overwrite != null)
                throw new InvalidDataException($"'{method.Name}' cannot be both a shadow and an overwrite.");
            if (shadow != null || overwrite != null)
            {
                if (InjectorAttributes(method).Any())
                    throw new InvalidDataException($"'{method.Name}' cannot combine a shadow or overwrite with an injector or wrapper.");
                string selector = Text(shadow ?? overwrite!, 0) ?? method.Name;
                var existing = originalMethods[target].SingleOrDefault(item => MemberMatches(item, selectors.Map(selector)) && Matches(method, item))
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
            var copy = new MethodDefinition(name, flags, game.TypeSystem.Void);
            CopyGenericParameters(method, copy, ImportType);
            TypeReference ImportSignature(TypeReference type) => ImportType(RebindMethodParameters(type, method, copy));
            copy.ReturnType = ImportSignature(method.ReturnType);
            foreach (ParameterDefinition parameter in method.Parameters)
                copy.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, ImportSignature(parameter.ParameterType)));
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
        source.Parameters.Count == target.Parameters.Count && source.Parameters.Zip(target.Parameters).All(pair =>
            SameParameter(ImportType(pair.First.ParameterType), pair.First.Attributes, pair.Second.ParameterType, pair.Second.Attributes));

    private bool Matches(MethodDefinition source, MethodDefinition target) =>
        target.IsStatic == source.IsStatic && !target.HasGenericParameters
        && Same(ImportType(source.ReturnType), target.ReturnType) && SameParameters(source, target);

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
            "Aether.Modding.ModifyArgAttribute" or "Aether.Modding.ModifyConstantAttribute" or
            "Aether.Modding.WrapMethodAttribute" or "Aether.Modding.WrapOperationAttribute");

    private static bool IsILEditor(CustomAttribute attribute) => attribute.AttributeType.FullName == "Aether.Modding.EditILAttribute";

    private static bool IsEditorMixin(TypeDefinition type) => type.Methods.Any(method => method.CustomAttributes.Any(IsILEditor));

    private static IOrderedEnumerable<Injection> Ordered(IEnumerable<Injection> edits) => edits.OrderBy(item => item.Order)
        .ThenByDescending(item => item.Declaration.Priority).ThenBy(item => item.Declaration.ModOrder)
        .ThenBy(item => item.Source.Name, StringComparer.Ordinal).ThenBy(item => item.Source.FullName, StringComparer.Ordinal)
        .ThenBy(item => item.Sequence);

    private void CopyMembers(Declaration declaration)
    {
        if (IsEditorMixin(declaration.Source))
        {
            foreach (MethodDefinition source in declaration.Source.Methods)
                foreach (CustomAttribute attribute in source.CustomAttributes.Where(IsILEditor))
                {
                    MethodDefinition target = ResolveBodyEdit(declaration, attribute);
                    ilEditors.Add(new Injection(declaration, source, target, attribute, Option(attribute, "Order", 1000), ilEditors.Count, source));
                }
            return;
        }
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
                if (attribute.AttributeType.Name == "WrapMethodAttribute")
                {
                    MethodDefinition wrapped = ResolveBodyEdit(declaration, attribute);
                    if (wrapped.IsConstructor || wrapped.CallingConvention == MethodCallingConvention.VarArg)
                        throw new InvalidDataException($"Unsupported wrapper target '{wrapped.FullName}'.");
                    methodWrappers.Add(new Injection(declaration, handler, wrapped, attribute, Option(attribute, "Order", 1000), methodWrappers.Count, source));
                    continue;
                }
                string selector = Text(attribute, 0)!;
                MethodDefinition? target = declaration.Selectors.Method(declaration.Target, originalMethods[declaration.Target], selector);
                if (target == null && Option(attribute, "Require", 1) == 0 &&
                    (!debug || Option(attribute, "Expect", 1) == 0))
                    continue;
                bool wrapsOperation = attribute.AttributeType.Name == "WrapOperationAttribute";
                bool callback = attribute.AttributeType.Name == "InjectAttribute";
                if (target == null || !target.HasBody || !wrapsOperation && handler.HasGenericParameters ||
                    callback && (target.HasGenericParameters || target.ReturnType.IsByReference))
                    throw new InvalidDataException($"Unsupported or missing injection target {declaration.Selectors.Describe(selector)}.");
                if (overwrites.ContainsKey(target))
                    throw new InvalidDataException($"Cannot inject into overwritten method '{target.FullName}'.");
                int order = Option(attribute, "Order", attribute.AttributeType.Name == "RedirectAttribute" ? 10000 : 1000);
                injections.Add(new Injection(declaration, handler, target, attribute, order, injections.Count, source));
            }
        }
    }

    private MethodDefinition ResolveBodyEdit(Declaration declaration, CustomAttribute attribute)
    {
        string selector = Text(attribute, 0)!;
        MethodDefinition? target = declaration.Selectors.Method(declaration.Target, originalMethods[declaration.Target], selector);
        if (target == null || !target.HasBody)
            throw new InvalidDataException($"Unsupported or missing target {declaration.Selectors.Describe(selector)}.");
        if (overwrites.TryGetValue(target, out Declaration? owner))
            throw new InvalidDataException($"Cannot edit '{target.FullName}' because it is overwritten by {owner.Name}.");
        return target;
    }
}
