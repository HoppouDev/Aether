using Aether.Modding;
using Mono.Cecil;
using Mono.Cecil.Cil;
using static Aether.Bootstrap.Selectors;

namespace Aether.Bootstrap;

internal sealed partial class AssemblyWeaver
{
    private void EmitModifyArg(Site site, Instruction anchor, List<Instruction> code)
    {
        MethodDefinition handler = site.Injection.Handler;
        MethodDefinition target = site.Injection.Target;
        var called = (MethodReference)anchor.Operand;
        if (called.CallingConvention == MethodCallingConvention.VarArg)
            throw new InvalidDataException($"Vararg call '{called.FullName}' cannot be modified.");
        TypeReference[] types = called.Parameters.Select(parameter => ImportType(Specialize(parameter.ParameterType, called.DeclaringType, called))).ToArray();
        int selected = Option(site.Injection.Attribute, "Index", -1);
        if (selected == -1)
        {
            var candidates = Enumerable.Range(0, types.Length).Where(index => Same(types[index], handler.ReturnType)).ToArray();
            if (candidates.Length != 1)
                throw new InvalidDataException($"Specify Index for '{handler.Name}'; the argument type does not identify a unique slot.");
            selected = candidates[0];
        }

        bool allArguments = handler.Parameters.Select(parameter => parameter.ParameterType.FullName)
            .SequenceEqual(types.Select(type => type.FullName), StringComparer.Ordinal);
        if (selected < 0 || selected >= types.Length || !Same(handler.ReturnType, types[selected]) ||
            !allArguments && (handler.Parameters.Count != 1 || !Same(handler.Parameters[0].ParameterType, types[selected])) ||
            handler.HasThis && !target.HasThis)
            throw new InvalidDataException($"Invalid argument modifier signature '{handler.FullName}' for '{called.FullName}'.");

        int start = allArguments ? 0 : selected;
        var locals = new VariableDefinition[types.Length];
        for (int index = types.Length - 1; index >= start; index--)
        {
            locals[index] = Local(target, types[index]);
            Add(code, OpCodes.Stloc, locals[index]);
        }
        if (handler.HasThis)
            Add(code, OpCodes.Ldarg_0);
        if (allArguments)
        {
            foreach (VariableDefinition local in locals)
                Add(code, OpCodes.Ldloc, local);
        }
        else
        {
            Add(code, OpCodes.Ldloc, locals[selected]);
        }

        Add(code, OpCodes.Call, handler);
        Add(code, OpCodes.Stloc, locals[selected]);
        for (int index = start; index < types.Length; index++)
            Add(code, OpCodes.Ldloc, locals[index]);
    }

    private void EmitModifyConstant(Site site, Instruction anchor, List<Instruction> code)
    {
        MethodDefinition handler = site.Injection.Handler;
        MethodDefinition target = site.Injection.Target;
        MetadataType type = Literal(anchor)!.Value.Type;
        if (handler.Parameters.Count != 1 || !Same(handler.Parameters[0].ParameterType, handler.ReturnType)
            || (type == MetadataType.Object
                ? !MatchesNullType(anchor, target, handler.ReturnType)
                : handler.ReturnType.MetadataType != type)
            || handler.HasThis && !target.HasThis)
            throw new InvalidDataException($"Invalid constant modifier signature '{handler.FullName}' for '{Literal(anchor)!.Value.Selector}'.");

        if (handler.HasThis)
        {
            VariableDefinition value = Local(target, handler.ReturnType);
            Add(code, OpCodes.Stloc, value);
            Add(code, OpCodes.Ldarg_0);
            Add(code, OpCodes.Ldloc, value);
        }
        Add(code, OpCodes.Call, handler);
    }

    private static bool MatchesNullType(Instruction anchor, MethodDefinition target, TypeReference candidate)
    {
        if (candidate.IsValueType || candidate.IsByReference || candidate.IsPointer)
            return false;

        Instruction? next = anchor.Next;
        while (next?.OpCode.Code == Code.Nop)
            next = next.Next;
        TypeReference? expected = next?.OpCode.Code switch
        {
            Code.Ret => target.ReturnType,
            Code.Stloc_0 => target.Body.Variables[0].VariableType,
            Code.Stloc_1 => target.Body.Variables[1].VariableType,
            Code.Stloc_2 => target.Body.Variables[2].VariableType,
            Code.Stloc_3 => target.Body.Variables[3].VariableType,
            Code.Stloc or Code.Stloc_S => ((VariableDefinition)next.Operand).VariableType,
            Code.Stfld or Code.Stsfld => ((FieldReference)next.Operand).FieldType,
            Code.Call or Code.Callvirt or Code.Newobj when next.Operand is MethodReference method && method.HasParameters
                => Specialize(method.Parameters[^1].ParameterType, method.DeclaringType, method),
            _ => null
        };
        return expected != null && Same(candidate, expected);
    }

    private void EmitRedirect(Site site, Instruction anchor, List<Instruction> code)
    {
        MethodDefinition handler = site.Injection.Handler;
        MethodDefinition target = site.Injection.Target;
        var (operands, result) = OperationSignature(target, anchor);

        if (!Same(handler.ReturnType, result) ||
            !handler.Parameters.Select(parameter => parameter.ParameterType.FullName)
                .SequenceEqual(operands.Select(operand => operand.FullName), StringComparer.Ordinal) ||
            handler.HasThis && !target.HasThis)
            throw new InvalidDataException($"Invalid redirect signature '{handler.FullName}' for '{anchor.Operand}'.");

        if (handler.HasThis)
        {
            var locals = operands.Select(operand => Local(target, operand)).ToArray();
            for (int index = locals.Length - 1; index >= 0; index--)
                Add(code, OpCodes.Stloc, locals[index]);
            Add(code, OpCodes.Ldarg_0);
            foreach (VariableDefinition local in locals)
                Add(code, OpCodes.Ldloc, local);
        }

        Add(code, OpCodes.Call, handler);
    }

    private (TypeReference[] Operands, TypeReference Result) OperationSignature(MethodDefinition target, Instruction anchor)
    {
        var operands = new List<TypeReference>();
        TypeReference result;
        if (anchor.Operand is MethodReference method)
        {
            if (method.CallingConvention == MethodCallingConvention.VarArg)
                throw new InvalidDataException($"Cannot replace vararg call '{method.FullName}'.");
            bool creates = anchor.OpCode.Code == Code.Newobj;
            if (method.HasThis && !creates)
            {
                TypeReference receiver = ImportType(method.DeclaringType);
                operands.Add(receiver.IsValueType ? new ByReferenceType(receiver) : receiver);
            }
            operands.AddRange(method.Parameters.Select(parameter => ImportType(Specialize(parameter.ParameterType, method.DeclaringType, method))));
            result = ImportType(creates ? method.DeclaringType : Specialize(method.ReturnType, method.DeclaringType, method));
        }
        else
        {
            var field = (FieldReference)anchor.Operand;
            bool reads = anchor.OpCode.Code is Code.Ldfld or Code.Ldsfld;
            if (anchor.OpCode.Code is Code.Ldfld or Code.Stfld)
            {
                TypeReference receiver = ImportType(field.DeclaringType);
                operands.Add(receiver.IsValueType ? StructFieldReceiver(target, anchor, receiver, reads) : receiver);
            }
            TypeReference type = ImportType(Specialize(field.FieldType, field.DeclaringType));
            if (!reads)
                operands.Add(type);
            result = reads ? type : game.TypeSystem.Void;
        }
        return (operands.ToArray(), result);
    }

    private static int?[] AnalyzeDepths(MethodDefinition method, Instruction[] instructions, bool verifyBody = false)
    {
        var indexes = instructions.Select((instruction, index) => (instruction, index))
            .ToDictionary(item => item.instruction, item => item.index);
        var depths = new int?[instructions.Length];
        var queue = new Queue<(int Index, int Depth)>();
        queue.Enqueue((0, 0));
        if (verifyBody)
            foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
            {
                queue.Enqueue((indexes[handler.HandlerStart], handler.HandlerType is ExceptionHandlerType.Catch or ExceptionHandlerType.Filter ? 1 : 0));
                if (handler.FilterStart != null)
                    queue.Enqueue((indexes[handler.FilterStart], 1));
            }
        while (queue.TryDequeue(out var current))
        {
            var (index, depth) = current;
            if (depths[index] is int known)
            {
                if (known != depth)
                    throw new InvalidDataException($"Inconsistent stack depth in '{method.FullName}'.");
                continue;
            }

            depths[index] = depth;
            Instruction instruction = instructions[index];
            int next = DepthAfter(method, instruction, depth)!.Value;
            switch (instruction.OpCode.FlowControl)
            {
                case FlowControl.Branch:
                    if (instruction.Operand is Instruction branch)
                        queue.Enqueue((indexes[branch], next));
                    continue;
                case FlowControl.Cond_Branch:
                    if (instruction.Operand is Instruction conditional)
                        queue.Enqueue((indexes[conditional], next));
                    else if (instruction.Operand is Instruction[] branches)
                        foreach (Instruction candidate in branches)
                            queue.Enqueue((indexes[candidate], next));
                    break;
                case FlowControl.Throw:
                    continue;
                case FlowControl.Return:
                    if (verifyBody && next != 0)
                        throw new InvalidDataException($"'{instruction.OpCode}' leaves values on the stack in '{method.FullName}'.");
                    continue;
            }

            if (index + 1 < instructions.Length)
                queue.Enqueue((index + 1, next));
            else if (verifyBody)
                throw new InvalidDataException($"Execution can fall off the end of '{method.FullName}'.");
        }
        return depths;
    }

    private static int? DepthAfter(MethodDefinition method, Instruction instruction, int? depth)
    {
        if (depth == null)
            return null;
        int remaining = depth.Value - PopCount(method, instruction, depth.Value);
        if (remaining < 0)
            throw new InvalidDataException($"Invalid stack depth before '{instruction.OpCode}' in '{method.FullName}'.");
        return remaining + PushCount(instruction);
    }

    private static int PopCount(MethodDefinition method, Instruction instruction, int depth) => instruction.OpCode.StackBehaviourPop switch
    {
        StackBehaviour.Pop0 => 0,
        StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
        StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
            or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1
            or StackBehaviour.Popref_popi => 2,
        StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8
            or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref => 3,
        StackBehaviour.PopAll => depth,
        StackBehaviour.Varpop when instruction.OpCode.Code == Code.Ret => method.ReturnType.MetadataType == MetadataType.Void ? 0 : 1,
        StackBehaviour.Varpop when instruction.Operand is MethodReference called =>
            called.Parameters.Count + (called.HasThis && instruction.OpCode.Code != Code.Newobj ? 1 : 0),
        StackBehaviour.Varpop when instruction.Operand is CallSite site => site.Parameters.Count + (site.HasThis ? 2 : 1),
        _ => throw new InvalidDataException($"Cannot analyze stack operation '{instruction.OpCode}' in '{method.FullName}'.")
    };

    private static int PushCount(Instruction instruction) => instruction.OpCode.StackBehaviourPush switch
    {
        StackBehaviour.Push0 => 0,
        StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4
            or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
        StackBehaviour.Push1_push1 => 2,
        StackBehaviour.Varpush when instruction.Operand is MethodReference called =>
            called.ReturnType.MetadataType == MetadataType.Void ? 0 : 1,
        StackBehaviour.Varpush when instruction.Operand is CallSite site => site.ReturnType.MetadataType == MetadataType.Void ? 0 : 1,
        _ => throw new InvalidDataException($"Cannot analyze stack operation '{instruction.OpCode}'.")
    };
}
