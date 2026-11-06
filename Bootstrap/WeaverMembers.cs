using Mono.Cecil;
using Mono.Cecil.Cil;
using static Aether.Bootstrap.Selectors;

namespace Aether.Bootstrap;

internal sealed partial class AssemblyWeaver
{
    private TypeReference ImportType(TypeReference source)
    {
        if (source is ArrayType array)
            return array.IsVector ? new ArrayType(ImportType(array.ElementType)) : new ArrayType(ImportType(array.ElementType), array.Rank);
        if (source is ByReferenceType reference)
            return new ByReferenceType(ImportType(reference.ElementType));
        if (source is PointerType pointer)
            return new PointerType(ImportType(pointer.ElementType));
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
        if (source.DeclaringType.Scope.Name == game.Assembly.Name.Name && game.GetType(source.DeclaringType.FullName) is { } type)
            return type.Fields.SingleOrDefault(field => field.FullName == source.FullName)
                ?? throw new InvalidDataException($"Cannot resolve game field '{source.FullName}'.");
        return TrackReference(game.ImportReference(source));
    }

    private T TrackReference<T>(T member) where T : MemberReference
    {
        switch (member)
        {
            case TypeReference type:
                TrackType(type);
                break;
            case MethodReference method:
                TrackType(method.DeclaringType);
                TrackType(method.ReturnType);
                foreach (ParameterDefinition parameter in method.Parameters)
                    TrackType(parameter.ParameterType);
                break;
            case FieldReference field:
                TrackType(field.DeclaringType);
                TrackType(field.FieldType);
                break;
        }
        return member;
    }

    private void TrackType(TypeReference type)
    {
        if (importingPackage == null || type is GenericParameter)
            return;

        if (type is GenericInstanceType generic)
            foreach (TypeReference argument in generic.GenericArguments)
                TrackType(argument);
        if (type is IModifierType modifier)
            TrackType(modifier.ModifierType);
        if (type is FunctionPointerType function)
        {
            TrackType(function.ReturnType);
            foreach (ParameterDefinition parameter in function.Parameters)
                TrackType(parameter.ParameterType);
        }
        else if (type is TypeSpecification specification)
        {
            TrackType(specification.ElementType);
        }
        else if (type.Scope is AssemblyNameReference reference)
        {
            if (!referenceOwners.TryGetValue(reference.FullName, out var owners))
                referenceOwners.Add(reference.FullName, owners = []);
            owners.Add(importingPackage);
        }
    }

    private void CloneBody(MethodDefinition source, MethodDefinition target)
    {
        if (!source.HasBody)
            throw new InvalidDataException($"Missing body for '{source.FullName}'.");
        MethodBody original = source.Body;
        var body = new MethodBody(target) { InitLocals = original.InitLocals, MaxStackSize = original.MaxStackSize };
        target.Body = body;
        foreach (VariableDefinition local in original.Variables)
        {
            var type = local.VariableType is PinnedType pinned
                ? new PinnedType(ImportType(pinned.ElementType)) : ImportType(local.VariableType);
            body.Variables.Add(new VariableDefinition(type));
        }

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
                MethodReference method => ImportMethod(method),
                FieldReference field => ImportField(field),
                TypeReference type => ImportType(type),
                CallSite => throw new InvalidDataException("Indirect calls in mixin bodies are unsupported."),
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
                CatchType = handler.CatchType == null ? null : ImportType(handler.CatchType)
            });
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
                var field = target.Fields.SingleOrDefault(item => MemberMatches(item, mapped) && item.IsStatic == isStatic &&
                    value != null && Same(item.FieldType, value) && (getter || !item.IsInitOnly && !item.IsLiteral))
                    ?? throw new InvalidDataException($"Cannot bind field accessor {selectors.Describe(selector)}.");
                if (field.IsLiteral)
                    throw new InvalidDataException($"Literal field '{field.Name}' has no runtime storage.");
                il.Emit(getter ? (isStatic ? OpCodes.Ldsfld : OpCodes.Ldfld) : (isStatic ? OpCodes.Stsfld : OpCodes.Stfld), field);
            }
            else
            {
                var called = target.Methods.SingleOrDefault(item => MemberMatches(item, mapped) &&
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
