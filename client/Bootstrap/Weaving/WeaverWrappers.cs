using Mono.Cecil;
using Mono.Cecil.Cil;
using static Aether.Bootstrap.Selectors;

using static Aether.Bootstrap.TypeSignatures;
namespace Aether.Bootstrap;

internal sealed partial class AssemblyWeaver
{
    private void ApplyMethodWrappers()
    {
        foreach (var group in methodWrappers.GroupBy(item => item.Target))
        {
            MethodDefinition target = group.Key;
            Injection[] wrappers = Ordered(group).ToArray();
            WithContext(wrappers[0].Declaration, () =>
            {
                TypeReference[] operands = target.Parameters.Select(parameter => parameter.ParameterType).ToArray();
                string name = $"__aether$method${target.DeclaringType.Methods.IndexOf(target)}";
                MethodDefinition original = CreateContinuation(target, name, operands, target.ReturnType,
                    target.Parameters.Select(parameter => parameter.Attributes).ToArray());
                CloneBody(target, original);
                MethodDefinition first = BuildWrapperChain(target, original, wrappers);
                target.Body = new MethodBody(target);
                var code = new List<Instruction>();
                LoadArguments(code, target, first.HasThis);
                Add(code, OpCodes.Call, InGenericContext(first, target));
                Add(code, OpCodes.Ret);
                Append(target, code);
            });
        }
    }

    private void EmitOperationWrappers(MethodDefinition target, Instruction anchor, int index,
        IReadOnlyList<Injection> wrappers, List<Instruction> code)
    {
        var (operands, result) = OperationSignature(target, anchor);
        if (anchor.Operand is FieldReference field && anchor.OpCode.Code is Code.Stfld or Code.Stsfld &&
            (field.Resolve() ?? throw new InvalidDataException($"Cannot resolve field '{field.FullName}'.")).IsInitOnly)
            throw new InvalidDataException($"Cannot wrap a write to readonly field '{field.FullName}'.");

        string name = $"__aether$operation${target.DeclaringType.Methods.IndexOf(target)}${index}";
        var attributes = new ParameterAttributes[operands.Length];
        if (anchor.Operand is MethodReference called && called.Parameters.Any(parameter => NormalizeIn(parameter.ParameterType).IsByReference))
        {
            var definition = called.Resolve() ?? throw new InvalidDataException($"Cannot resolve by-reference parameters of '{called.FullName}'.");
            int offset = operands.Length - definition.Parameters.Count;
            for (int i = 0; i < definition.Parameters.Count; i++)
                attributes[i + offset] = definition.Parameters[i].Attributes;
        }
        MethodDefinition original = CreateContinuation(target, name, operands, result, attributes, hasThis: wrappers.Any(wrapper => wrapper.Handler.HasThis));
        var operation = new List<Instruction>();
        LoadArguments(operation, original, receiver: false);
        Add(operation, anchor.OpCode, anchor.Operand);
        Add(operation, OpCodes.Ret);
        Append(original, operation);

        MethodDefinition first = BuildWrapperChain(target, original, wrappers);
        if (first.HasThis)
        {
            var locals = operands.Select(type => Local(target, type)).ToArray();
            for (int i = locals.Length - 1; i >= 0; i--)
                Add(code, OpCodes.Stloc, locals[i]);
            Add(code, OpCodes.Ldarg_0);
            foreach (VariableDefinition local in locals)
                Add(code, OpCodes.Ldloc, local);
        }
        Add(code, OpCodes.Call, InGenericContext(first, target));
    }

    private MethodDefinition BuildWrapperChain(MethodDefinition target, MethodDefinition original, IReadOnlyList<Injection> wrappers)
    {
        TypeReference[] operands = original.Parameters.Select(parameter => parameter.ParameterType).ToArray();
        MethodDefinition next = original;
        for (int index = wrappers.Count - 1; index >= 0; index--)
        {
            Injection wrapper = wrappers[index];
            int ordinal = index;
            WithContext(wrapper.Declaration, () =>
            {
                MethodDefinition handler = wrapper.Handler;
                TypeReference MatchType(TypeReference type) => RebindMethodParameters(type, handler, original);
                if (!SameGenericConstraints(handler, original) || handler.HasThis && !target.HasThis ||
                    handler.Parameters.Count != operands.Length + 1 || !Same(MatchType(handler.ReturnType), original.ReturnType) ||
                    !handler.Parameters.Skip(1).Zip(original.Parameters).All(pair =>
                        SameParameter(MatchType(pair.First.ParameterType), pair.First.Attributes, pair.Second.ParameterType, pair.Second.Attributes)))
                    throw new InvalidDataException($"Invalid wrapper signature '{handler.FullName}' for '{target.FullName}'.");

                var bridge = CreateContinuation(original, $"{original.Name}${ordinal}", operands, original.ReturnType,
                    original.Parameters.Select(parameter => parameter.Attributes).ToArray());
                TypeReference delegateType = RebindMethodParameters(handler.Parameters[0].ParameterType, handler, bridge);
                MethodReference constructor = DelegateConstructor(wrapper.Source, delegateType, original, target);
                var code = new List<Instruction>();
                if (handler.HasThis)
                    Add(code, OpCodes.Ldarg_0);
                Add(code, next.HasThis ? OpCodes.Ldarg_0 : OpCodes.Ldnull);
                Add(code, OpCodes.Ldftn, InGenericContext(next, bridge));
                Add(code, OpCodes.Newobj, constructor);
                LoadArguments(code, bridge, receiver: false);
                Add(code, OpCodes.Call, InGenericContext(handler, bridge));
                Add(code, OpCodes.Ret);
                Append(bridge, code);
                next = bridge;
            });
        }
        return next;
    }

    private static bool SameGenericConstraints(MethodDefinition handler, MethodDefinition original)
    {
        if (handler.GenericParameters.Count != original.GenericParameters.Count)
            return false;
        for (int i = 0; i < handler.GenericParameters.Count; i++)
        {
            GenericParameter actual = handler.GenericParameters[i];
            GenericParameter expected = original.GenericParameters[i];
            if (actual.Attributes != expected.Attributes || actual.Constraints.Count != expected.Constraints.Count ||
                actual.Constraints.Any(constraint => !expected.Constraints.Any(candidate =>
                    Same(RebindMethodParameters(constraint.ConstraintType, handler, original), candidate.ConstraintType))))
                return false;
        }
        return true;
    }

    private MethodReference DelegateConstructor(MethodDefinition sourceHandler, TypeReference type,
        MethodDefinition original, MethodDefinition target)
    {
        TypeReference source = sourceHandler.Parameters[0].ParameterType;
        TypeDefinition? definition = source.Resolve();
        MethodDefinition? invoke = definition?.Methods.SingleOrDefault(method => method.Name == "Invoke");
        TypeReference MatchType(TypeReference argument) => ImportType(RebindMethodParameters(Specialize(argument, source), sourceHandler, original));
        if (definition?.BaseType?.FullName != "System.MulticastDelegate" || !IsPublicType(definition) || invoke == null ||
            !Same(MatchType(invoke.ReturnType), original.ReturnType) ||
            invoke.Parameters.Count != original.Parameters.Count || !invoke.Parameters.Zip(original.Parameters).All(pair =>
                SameParameter(MatchType(pair.First.ParameterType), pair.First.Attributes, pair.Second.ParameterType, pair.Second.Attributes)))
            throw new InvalidDataException($"Continuation delegate '{source.FullName}' must match the wrapped signature in '{target.FullName}' and be public.");

        var constructor = new MethodReference(".ctor", game.TypeSystem.Void, type) { HasThis = true };
        constructor.Parameters.Add(new ParameterDefinition(game.TypeSystem.Object));
        constructor.Parameters.Add(new ParameterDefinition(game.TypeSystem.IntPtr));
        return constructor;
    }

    private static bool IsPublicType(TypeDefinition type) => type.DeclaringType is { } declaring
        ? type.IsNestedPublic && IsPublicType(declaring)
        : type.IsPublic;

    private static MethodDefinition CreateContinuation(MethodDefinition target, string name, TypeReference[] operands,
        TypeReference result, ParameterAttributes[] attributes, bool? hasThis = null)
    {
        if (target.DeclaringType.Methods.Any(method => method.Name == name))
            throw new InvalidDataException($"Generated wrapper method name '{name}' is already in use.");
        var flags = MethodAttributes.Private | MethodAttributes.HideBySig;
        if (!(hasThis ?? target.HasThis))
            flags |= MethodAttributes.Static;
        var method = new MethodDefinition(name, flags, result);
        CopyGenericParameters(target, method, type => type);
        method.ReturnType = RebindMethodParameters(result, target, method);
        for (int i = 0; i < operands.Length; i++)
            method.Parameters.Add(new ParameterDefinition(null, attributes[i] & (ParameterAttributes.In | ParameterAttributes.Out),
                RebindMethodParameters(operands[i], target, method)));
        target.DeclaringType.Methods.Add(method);
        return method;
    }

    private static MethodReference InGenericContext(MethodDefinition method, MethodDefinition context)
    {
        if (!method.HasGenericParameters)
            return method;
        var instance = new GenericInstanceMethod(method);
        foreach (GenericParameter parameter in context.GenericParameters)
            instance.GenericArguments.Add(parameter);
        return instance;
    }

    private static void LoadArguments(List<Instruction> code, MethodDefinition method, bool receiver)
    {
        if (receiver)
            Add(code, OpCodes.Ldarg_0);
        foreach (ParameterDefinition parameter in method.Parameters)
            Add(code, OpCodes.Ldarg, parameter);
    }

    private static void Append(MethodDefinition method, List<Instruction> code)
    {
        foreach (Instruction instruction in code)
            method.Body.Instructions.Add(instruction);
    }
}
