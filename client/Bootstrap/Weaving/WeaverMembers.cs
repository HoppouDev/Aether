using Mono.Cecil;
using Mono.Cecil.Cil;
using static Aether.Bootstrap.Selectors;

using static Aether.Bootstrap.TypeSignatures;
namespace Aether.Bootstrap;

internal sealed partial class AssemblyWeaver
{
    private TypeReference ImportType(TypeReference source)
    {
        if (source is GenericParameter)
            return source;
        if (source is FunctionPointerType function)
            return MapFunctionPointer(function, ImportType);
        if (source is ArrayType array)
            return array.IsVector ? new ArrayType(ImportType(array.ElementType)) : new ArrayType(ImportType(array.ElementType), array.Rank);
        if (source is ByReferenceType reference)
            return new ByReferenceType(ImportType(reference.ElementType));
        if (source is PointerType pointer)
            return new PointerType(ImportType(pointer.ElementType));
        if (source is RequiredModifierType required)
            return new RequiredModifierType(ImportType(required.ModifierType), ImportType(required.ElementType));
        if (source is OptionalModifierType optional)
            return new OptionalModifierType(ImportType(optional.ModifierType), ImportType(optional.ElementType));
        if (source is GenericInstanceType generic)
        {
            var instance = new GenericInstanceType(ImportType(generic.ElementType));
            foreach (TypeReference argument in generic.GenericArguments)
                instance.GenericArguments.Add(ImportType(argument));
            return instance;
        }
        if (source is TypeDefinition definition && mixinTypes.TryGetValue(definition, out Declaration? declaration) && !definition.IsInterface)
            return declaration.Target;
        if (source.Scope.Name == game.Assembly.Name.Name && game.GetType(source.FullName) is { } gameType)
            return gameType;
        return TrackReference(game.ImportReference(source));
    }

    private MethodReference ImportMethod(MethodReference source)
    {
        if (source is GenericInstanceMethod generic)
        {
            var instance = new GenericInstanceMethod(ImportMethod(generic.ElementMethod));
            foreach (TypeReference argument in generic.GenericArguments)
                instance.GenericArguments.Add(ImportType(argument));
            return instance;
        }

        if (source is MethodDefinition definition && methods.TryGetValue(definition, out MethodDefinition? merged))
            return merged;
        if (source.DeclaringType is TypeDefinition mixin && mixinTypes.ContainsKey(mixin) && !mixin.IsInterface)
            throw new InvalidDataException($"Cannot reference unmerged mixin method '{source.FullName}'.");

        if (source.DeclaringType is GenericInstanceType declaring)
        {
            var reference = new MethodReference(source.Name, game.TypeSystem.Void, ImportType(declaring))
            {
                HasThis = source.HasThis,
                ExplicitThis = source.ExplicitThis,
                CallingConvention = source.CallingConvention
            };
            foreach (GenericParameter parameter in source.GenericParameters)
                reference.GenericParameters.Add(new GenericParameter(parameter.Name, reference));
            TypeReference ImportSignature(TypeReference type) => ImportType(ReplaceGenericParameters(type, parameter =>
                parameter.Type == GenericParameterType.Method && parameter.Owner == source ? reference.GenericParameters[parameter.Position] : parameter));
            reference.ReturnType = ImportSignature(source.ReturnType);
            foreach (ParameterDefinition parameter in source.Parameters)
                reference.Parameters.Add(new ParameterDefinition(ImportSignature(parameter.ParameterType)));
            return reference;
        }

        if (source.DeclaringType.Scope.Name == game.Assembly.Name.Name && game.GetType(source.DeclaringType.FullName) is { } type)
            return type.Methods.SingleOrDefault(method => method.FullName == source.FullName)
                ?? throw new InvalidDataException($"Cannot resolve game method '{source.FullName}'.");
        return TrackReference(game.ImportReference(source));
    }

    private FieldReference ImportField(FieldReference source)
    {
        if (source is FieldDefinition definition && fields.TryGetValue(definition, out FieldDefinition? merged))
            return merged;
        if (source.DeclaringType is TypeDefinition mixin && mixinTypes.ContainsKey(mixin))
            throw new InvalidDataException($"Cannot reference unmerged mixin field '{source.FullName}'.");

        if (source.DeclaringType is GenericInstanceType declaring)
            return new FieldReference(source.Name, ImportType(source.FieldType), ImportType(declaring));
        if (source.DeclaringType.Scope.Name == game.Assembly.Name.Name && game.GetType(source.DeclaringType.FullName) is { } type)
            return type.Fields.SingleOrDefault(field => field.FullName == source.FullName)
                ?? throw new InvalidDataException($"Cannot resolve game field '{source.FullName}'.");
        return TrackReference(game.ImportReference(source));
    }

    private T TrackReference<T>(T member) where T : MemberReference
    {
        foreach (TypeReference type in ReferencedTypes(member))
            TrackType(type);
        return member;
    }

    private static IEnumerable<TypeReference> ReferencedTypes(object? operand) => operand switch
    {
        TypeReference type => [type],
        GenericInstanceMethod method => new[] { method.DeclaringType, method.ReturnType }
            .Concat(method.Parameters.Select(parameter => parameter.ParameterType)).Concat(method.GenericArguments),
        MethodReference method => new[] { method.DeclaringType, method.ReturnType }
            .Concat(method.Parameters.Select(parameter => parameter.ParameterType)),
        FieldReference field => [field.DeclaringType, field.FieldType],
        CallSite site => new[] { site.ReturnType }.Concat(site.Parameters.Select(parameter => parameter.ParameterType)),
        _ => []
    };

    private static IEnumerable<AssemblyNameReference> ReferencedAssemblies(TypeReference type)
    {
        if (type is GenericParameter)
            yield break;

        if (type is GenericInstanceType generic)
            foreach (AssemblyNameReference name in generic.GenericArguments.SelectMany(ReferencedAssemblies))
                yield return name;
        if (type is IModifierType modifier)
            foreach (AssemblyNameReference name in ReferencedAssemblies(modifier.ModifierType))
                yield return name;
        if (type is FunctionPointerType function)
        {
            foreach (AssemblyNameReference name in ReferencedAssemblies(function.ReturnType)
                .Concat(function.Parameters.SelectMany(parameter => ReferencedAssemblies(parameter.ParameterType))))
                yield return name;
        }
        else if (type is TypeSpecification specification)
        {
            foreach (AssemblyNameReference name in ReferencedAssemblies(specification.ElementType))
                yield return name;
        }
        else if (type.Scope is AssemblyNameReference reference)
            yield return reference;
    }

    private void TrackType(TypeReference type)
    {
        if (importingPackage != null)
            foreach (AssemblyNameReference reference in ReferencedAssemblies(type))
                TrackAssembly(reference);
    }

    private void TrackAssembly(AssemblyNameReference reference)
    {
        if (importingPackage == null)
            return;
        if (!referenceOwners.TryGetValue(reference.FullName, out var owners))
            referenceOwners.Add(reference.FullName, owners = []);
        owners.Add(importingPackage);
    }

    private void CloneBody(MethodDefinition source, MethodDefinition target)
    {
        if (!source.HasBody)
            throw new InvalidDataException($"Missing body for '{source.FullName}'.");
        MethodBody original = source.Body;
        bool sameModule = source.Module == game;
        TypeReference CopyType(TypeReference type)
        {
            type = RebindMethodParameters(type, source, target);
            if (sameModule)
                return type;
            return type is PinnedType pinned ? new PinnedType(ImportType(pinned.ElementType)) : ImportType(type);
        }
        var body = new MethodBody(target) { InitLocals = original.InitLocals, MaxStackSize = original.MaxStackSize };
        target.Body = body;
        foreach (VariableDefinition local in original.Variables)
            body.Variables.Add(new VariableDefinition(CopyType(local.VariableType)));

        var instructions = new Dictionary<Instruction, Instruction>();
        foreach (Instruction instruction in original.Instructions)
        {
            Instruction copy = Instruction.Create(OpCodes.Nop);
            copy.OpCode = LongBranch(instruction.OpCode);
            instructions.Add(instruction, copy);
            body.Instructions.Add(copy);
        }

        foreach (var (instruction, copy) in instructions)
            copy.Operand = instruction.Operand switch
            {
                Instruction branch => instructions[branch],
                Instruction[] branches => branches.Select(branch => instructions[branch]).ToArray(),
                ParameterDefinition parameter => parameter.Index == -1 ? body.ThisParameter : target.Parameters[parameter.Index],
                VariableDefinition local => body.Variables[local.Index],
                MethodReference method => sameModule ? method : ImportMethod(method),
                FieldReference field => sameModule ? field : ImportField(field),
                TypeReference type => CopyType(type),
                CallSite when !sameModule => throw new InvalidDataException("Indirect calls in mixin bodies are unsupported."),
                _ => instruction.Operand
            };

        Instruction? Map(Instruction? instruction) => instruction == null ? null : instructions[instruction];
        foreach (ExceptionHandler handler in original.ExceptionHandlers)
            body.ExceptionHandlers.Add(new ExceptionHandler(handler.HandlerType)
            {
                TryStart = Map(handler.TryStart),
                TryEnd = Map(handler.TryEnd),
                HandlerStart = Map(handler.HandlerStart),
                HandlerEnd = Map(handler.HandlerEnd),
                FilterStart = Map(handler.FilterStart),
                CatchType = handler.CatchType == null ? null : CopyType(handler.CatchType)
            });
    }

    private static TypeReference RebindMethodParameters(TypeReference type, MethodDefinition from, MethodDefinition to) =>
        !from.HasGenericParameters ? type : ReplaceGenericParameters(type, parameter =>
            parameter.Type == GenericParameterType.Method && parameter.Owner == from ? to.GenericParameters[parameter.Position] : parameter);

    private static void CopyGenericParameters(MethodDefinition source, MethodDefinition target, Func<TypeReference, TypeReference> import)
    {
        foreach (GenericParameter parameter in source.GenericParameters)
            target.GenericParameters.Add(new GenericParameter(parameter.Name, target) { Attributes = parameter.Attributes });
        for (int i = 0; i < source.GenericParameters.Count; i++)
            foreach (GenericParameterConstraint constraint in source.GenericParameters[i].Constraints)
                target.GenericParameters[i].Constraints.Add(new GenericParameterConstraint(
                    import(RebindMethodParameters(constraint.ConstraintType, source, target))));
    }

    private void AddAccessors(Declaration declaration)
    {
        var (package, selectors, source, target, _, _) = declaration;
        if (!source.IsPublic && !source.IsNestedPublic || source.HasInterfaces || source.HasProperties || source.HasEvents)
            throw new InvalidDataException("Accessor mixins must be public interfaces containing only declared methods.");

        bool? staticInterface = null;
        foreach (MethodDefinition method in source.Methods)
        {
            var accessor = Attribute(method, "Accessor");
            var invoker = Attribute(method, "Invoker");
            if ((accessor == null) == (invoker == null) || method.HasGenericParameters || method.IsStatic)
                throw new InvalidDataException($"'{method.Name}' needs one accessor or invoker declaration on an instance interface method.");

            CustomAttribute attribute = accessor ?? invoker!;
            string selector = Text(attribute, 0)!;
            string mapped = selectors.Map(selector);
            bool isStatic = Option(attribute, "IsStatic", false) || invoker != null && mapped.StartsWith(".ctor", StringComparison.Ordinal);
            if (staticInterface != null && staticInterface != isStatic)
                throw new InvalidDataException("Put static and instance accessors in separate interfaces.");
            staticInterface = isStatic;
            MethodAttributes flags = MethodAttributes.HideBySig | MethodAttributes.Public |
                (isStatic ? MethodAttributes.Static : MethodAttributes.Final | MethodAttributes.Virtual | MethodAttributes.NewSlot);
            string name = $"__aether$access${source.FullName.Replace('/', '+')}${method.Name}";
            var bridge = new MethodDefinition(name, flags, ImportType(method.ReturnType));
            foreach (ParameterDefinition parameter in method.Parameters)
                bridge.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, ImportType(parameter.ParameterType)));
            if (target.Methods.Any(item => item.Name == name && SameParameters(method, item)))
                throw new InvalidDataException($"Duplicate bridge '{name}'.");
            target.Methods.Add(bridge);
            if (!isStatic)
            {
                bridge.Overrides.Add(TrackReference(game.ImportReference(method)));
            }

            ILProcessor il = bridge.Body.GetILProcessor();
            if (!isStatic)
                il.Emit(OpCodes.Ldarg_0);
            foreach (ParameterDefinition parameter in bridge.Parameters)
                il.Emit(OpCodes.Ldarg, parameter);

            if (accessor != null)
            {
                bool getter = bridge.Parameters.Count == 0 && bridge.ReturnType.MetadataType != MetadataType.Void;
                bool setter = bridge.Parameters.Count == 1 && bridge.ReturnType.MetadataType == MetadataType.Void;
                TypeReference? value = getter ? bridge.ReturnType : setter ? bridge.Parameters[0].ParameterType : null;
                var field = originalFields[target].SingleOrDefault(item => MemberMatches(item, mapped) && item.IsStatic == isStatic &&
                    value != null && Same(item.FieldType, value) && (getter || !item.IsInitOnly && !item.IsLiteral))
                    ?? throw new InvalidDataException($"Cannot bind field accessor {selectors.Describe(selector)}.");
                if (field.IsLiteral)
                    throw new InvalidDataException($"Literal field '{field.Name}' has no runtime storage.");
                il.Emit(getter ? (isStatic ? OpCodes.Ldsfld : OpCodes.Ldfld) : (isStatic ? OpCodes.Stsfld : OpCodes.Stfld), field);
            }
            else
            {
                var called = originalMethods[target].SingleOrDefault(item => MemberMatches(item, mapped) &&
                    !item.HasGenericParameters && SameParameters(method, item) &&
                    (item.IsConstructor
                        ? !item.IsStatic && isStatic && Same(bridge.ReturnType, target)
                        : item.IsStatic == isStatic && Same(item.ReturnType, bridge.ReturnType)))
                    ?? throw new InvalidDataException($"Cannot bind invoker {selectors.Describe(selector)}.");
                il.Emit(called.IsConstructor ? OpCodes.Newobj : called.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, called);
            }
            il.Emit(OpCodes.Ret);
        }

        if (staticInterface == false)
            target.Interfaces.Add(new InterfaceImplementation(TrackReference(game.ImportReference(source))));
    }

    private static OpCode LongBranch(OpCode op) => op.Code switch
    {
        Code.Br_S => OpCodes.Br,
        Code.Brfalse_S => OpCodes.Brfalse,
        Code.Brtrue_S => OpCodes.Brtrue,
        Code.Beq_S => OpCodes.Beq,
        Code.Bge_S => OpCodes.Bge,
        Code.Bgt_S => OpCodes.Bgt,
        Code.Ble_S => OpCodes.Ble,
        Code.Blt_S => OpCodes.Blt,
        Code.Bne_Un_S => OpCodes.Bne_Un,
        Code.Bge_Un_S => OpCodes.Bge_Un,
        Code.Bgt_Un_S => OpCodes.Bgt_Un,
        Code.Ble_Un_S => OpCodes.Ble_Un,
        Code.Blt_Un_S => OpCodes.Blt_Un,
        Code.Leave_S => OpCodes.Leave,
        _ => op
    };
}
