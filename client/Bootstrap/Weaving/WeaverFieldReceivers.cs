using Mono.Cecil;
using Mono.Cecil.Cil;
using static Aether.Bootstrap.Selectors;

using static Aether.Bootstrap.TypeSignatures;
namespace Aether.Bootstrap;

internal sealed partial class AssemblyWeaver
{
    private readonly Dictionary<MethodDefinition, Dictionary<Instruction, List<Instruction>>> fieldPredecessors = new();

    private TypeReference StructFieldReceiver(MethodDefinition method, Instruction access, TypeReference receiver, bool reads)
    {
        if (!fieldPredecessors.TryGetValue(method, out var predecessors))
        {
            var instructions = method.Body.Instructions;
            predecessors = instructions.ToDictionary(instruction => instruction, _ => new List<Instruction>());
            foreach (Instruction instruction in instructions)
            {
                if (instruction.Operand is Instruction target)
                    predecessors[target].Add(instruction);
                else if (instruction.Operand is Instruction[] targets)
                    foreach (Instruction branch in targets)
                        predecessors[branch].Add(instruction);
                if (instruction.Next is { } next && instruction.OpCode.FlowControl is not
                    (FlowControl.Branch or FlowControl.Return or FlowControl.Throw))
                    predecessors[next].Add(instruction);
            }
            foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
            {
                predecessors[handler.HandlerStart].Clear();
                if (handler.FilterStart != null)
                    predecessors[handler.FilterStart].Clear();
            }
            fieldPredecessors.Add(method, predecessors);
        }

        var pending = new Queue<(Instruction Instruction, int Slot)>();
        var visited = new HashSet<(Instruction, int)>();
        pending.Enqueue((access, reads ? 0 : 1));
        TypeReference? verified = null;
        while (pending.TryDequeue(out var state))
        {
            if (!visited.Add(state))
                continue;
            if (state.Slot >= method.Body.MaxStackSize || predecessors[state.Instruction].Count == 0)
                throw Unverified();

            foreach (Instruction predecessor in predecessors[state.Instruction])
            {
                if (predecessor.OpCode.StackBehaviourPop == StackBehaviour.PopAll)
                    throw Unverified();
                if (predecessor.OpCode.Code == Code.Dup && state.Slot < 2)
                {
                    pending.Enqueue((predecessor, 0));
                    continue;
                }

                int pushed = PushCount(predecessor);
                if (state.Slot >= pushed)
                {
                    pending.Enqueue((predecessor, state.Slot - pushed + PopCount(method, predecessor, 0)));
                    continue;
                }

                TypeReference? produced = StripModifiers(ProducedType(method, predecessor));
                TypeReference? form = produced switch
                {
                    ByReferenceType address when StripModifiers(address.ElementType) is { } element && Same(element, receiver)
                        => new ByReferenceType(receiver),
                    PointerType pointer when Same(pointer.ElementType, receiver) => new PointerType(receiver),
                    _ when reads && produced != null && Same(produced, receiver) => receiver,
                    _ => null
                };
                if (form == null || verified != null && !Same(verified, form))
                    throw Unverified();
                verified = form;
            }
        }
        return verified ?? throw Unverified();

        InvalidDataException Unverified() => new($"Cannot verify the receiver stack type for '{access.Operand}' at IL_{access.Offset:x4} in '{method.FullName}'.");
    }

    private static TypeReference? StripModifiers(TypeReference? type) => type switch
    {
        RequiredModifierType required => StripModifiers(required.ElementType),
        OptionalModifierType optional => StripModifiers(optional.ElementType),
        PinnedType pinned => StripModifiers(pinned.ElementType),
        _ => type
    };

    private static TypeReference? ProducedType(MethodDefinition method, Instruction instruction)
    {
        TypeReference Argument(int index) => method.HasThis
            ? index == 0 ? method.Body.ThisParameter.ParameterType : method.Parameters[index - 1].ParameterType
            : method.Parameters[index].ParameterType;

        return instruction.OpCode.Code switch
        {
            Code.Ldarg_0 => Argument(0),
            Code.Ldarg_1 => Argument(1),
            Code.Ldarg_2 => Argument(2),
            Code.Ldarg_3 => Argument(3),
            Code.Ldarg or Code.Ldarg_S => ((ParameterDefinition)instruction.Operand).ParameterType,
            Code.Ldarga or Code.Ldarga_S => new ByReferenceType(((ParameterDefinition)instruction.Operand).ParameterType),
            Code.Ldloc_0 => method.Body.Variables[0].VariableType,
            Code.Ldloc_1 => method.Body.Variables[1].VariableType,
            Code.Ldloc_2 => method.Body.Variables[2].VariableType,
            Code.Ldloc_3 => method.Body.Variables[3].VariableType,
            Code.Ldloc or Code.Ldloc_S => ((VariableDefinition)instruction.Operand).VariableType,
            Code.Ldloca or Code.Ldloca_S => new ByReferenceType(((VariableDefinition)instruction.Operand).VariableType),
            Code.Call or Code.Callvirt when instruction.Operand is MethodReference called => Specialize(called.ReturnType, called.DeclaringType, called),
            Code.Calli when instruction.Operand is CallSite site => site.ReturnType,
            Code.Newobj => ((MethodReference)instruction.Operand).DeclaringType,
            Code.Ldfld or Code.Ldsfld when instruction.Operand is FieldReference field => Specialize(field.FieldType, field.DeclaringType),
            Code.Ldflda or Code.Ldsflda when instruction.Operand is FieldReference field => new ByReferenceType(Specialize(field.FieldType, field.DeclaringType)),
            Code.Ldobj or Code.Unbox_Any or Code.Ldelem_Any => (TypeReference)instruction.Operand,
            Code.Unbox or Code.Ldelema or Code.Refanyval => new ByReferenceType((TypeReference)instruction.Operand),
            _ => null
        };
    }
}
