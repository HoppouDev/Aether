using Mono.Cecil;
using Mono.Cecil.Cil;
using static Aether.Bootstrap.Selectors;

namespace Aether.Bootstrap;

internal sealed partial class AssemblyWeaver
{
    private readonly Dictionary<MethodDefinition, HashSet<Instruction>> stackEntries = new();

    private TypeReference StructFieldReceiver(MethodDefinition method, Instruction access, TypeReference receiver, bool reads)
    {
        if (!stackEntries.TryGetValue(method, out var entries))
        {
            entries = new HashSet<Instruction>();
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.Operand is Instruction target)
                    entries.Add(target);
                else if (instruction.Operand is Instruction[] targets)
                    entries.UnionWith(targets);
            }
            foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
            {
                entries.Add(handler.HandlerStart);
                if (handler.FilterStart != null)
                    entries.Add(handler.FilterStart);
            }
            stackEntries.Add(method, entries);
        }

        // Trace the receiver's stack slot backwards until a producer or a control-flow join.
        int slot = reads ? 0 : 1;
        for (Instruction current = access; !entries.Contains(current) && current.Previous is { } previous; current = previous)
        {
            if (previous.OpCode.FlowControl is FlowControl.Branch or FlowControl.Return or FlowControl.Throw)
                break;
            if (previous.OpCode.Code == Code.Dup && slot < 2)
            {
                slot = 0;
                continue;
            }

            int pushed = PushCount(previous);
            if (slot >= pushed)
            {
                slot += PopCount(method, previous, 0) - pushed;
                continue;
            }

            TypeReference? produced = StripModifiers(ProducedType(method, previous));
            if (produced is ByReferenceType address && StripModifiers(address.ElementType) is { } element && Same(element, receiver))
                return new ByReferenceType(receiver);
            if (reads && produced != null && Same(produced, receiver))
                return receiver;
            break;
        }

        throw new InvalidDataException($"Cannot verify the receiver stack type for '{access.Operand}' at IL_{access.Offset:x4} in '{method.FullName}'.");
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
