using Aether.Modding;
using Mono.Cecil;
using Mono.Cecil.Cil;
using static Aether.Bootstrap.Selectors;

namespace Aether.Bootstrap;

internal sealed partial class AssemblyWeaver
{
    private sealed record Site(Injection Injection, int Index, At Point, bool After, bool Entry);
    private sealed record Region(int Start, int End);

    private void ApplyInjections()
    {
        foreach (var group in injections.GroupBy(item => item.Target))
        {
            MethodDefinition method = group.Key;
            Instruction[] instructions = method.Body.Instructions.ToArray();
            originalLocalCounts[method] = method.Body.Variables.Count;
            var sites = new List<Site>();
            foreach (Injection injection in group.OrderBy(item => item.Order)
                .ThenByDescending(item => item.Declaration.Priority).ThenBy(item => item.Declaration.ModOrder)
                .ThenBy(item => item.Handler.FullName, StringComparer.Ordinal).ThenBy(item => item.Sequence))
                WithContext(injection.Declaration, () => sites.AddRange(FindSites(injection, instructions)));

            if (sites.Count == 0)
                continue;

            foreach (Instruction instruction in instructions)
                instruction.OpCode = LongBranch(instruction.OpCode);
            int?[]? depths = sites.Any(site => Option(site.Injection.Attribute, "Cancellable", false))
                && !method.Body.HasExceptionHandlers ? AnalyzeDepths(method, instructions) : null;
            var edits = new List<(Instruction Anchor, List<Instruction> Code, bool After, bool Entry)>();
            foreach (var siteGroup in sites.GroupBy(site => site.Index))
            {
                int index = siteGroup.Key;
                Instruction anchor = instructions[index];
                var before = new List<Instruction>();
                var after = new List<Instruction>();
                var entry = new List<Instruction>();
                Site? redirect = null;
                foreach (Site site in siteGroup)
                {
                    WithContext(site.Injection.Declaration, () =>
                    {
                        switch (site.Injection.Attribute.AttributeType.Name)
                        {
                            case "InjectAttribute":
                                bool cancellable = Option(site.Injection.Attribute, "Cancellable", false);
                                int? depth = cancellable && depths != null
                                    ? site.After ? DepthAfter(method, anchor, depths[index]) : depths[index]
                                    : null;
                                EmitCallback(site, anchor, site.Entry ? entry : site.After ? after : before, depth);
                                break;
                            case "RedirectAttribute":
                                if (redirect != null)
                                    throw new InvalidDataException($"Redirect in {method.FullName} conflicts with {redirect.Injection.Declaration.Name}.");
                                redirect = site;
                                break;
                            case "ModifyArgAttribute":
                                EmitModifyArg(site, anchor, before);
                                break;
                            case "ModifyConstantAttribute":
                                EmitModifyConstant(site, anchor, after);
                                break;
                        }
                    });
                }
                if (redirect != null)
                {
                    Site chosen = redirect;
                    WithContext(chosen.Injection.Declaration, () => EmitRedirect(chosen, anchor, before));
                }
                edits.Add((anchor, entry, false, true));
                edits.Add((anchor, before, false, false));
                edits.Add((anchor, after, true, false));
            }

            foreach (var (anchor, code, after, entry) in edits)
                Insert(method.Body, anchor, code, after, entry);
        }
    }

    private IEnumerable<Site> FindSites(Injection injection, Instruction[] instructions)
    {
        var (declaration, handler, target, attribute, _, _) = injection;
        if (instructions.Length == 0)
            throw new InvalidDataException($"Empty body for '{target.FullName}'.");

        int start = ConstructorStart(target, instructions);
        var region = new Region(start, instructions.Length - 1);
        if (Option<string?>(attribute, "Slice", null) is { } id)
        {
            var slice = declaration.Source.CustomAttributes.SingleOrDefault(item =>
                item.AttributeType.FullName == "Aether.Modding.SliceAttribute" && Text(item, 0) == id)
                ?? throw new InvalidDataException($"Unknown slice '{id}' in '{target.FullName}'.");
            int from = ResolveAnchor(slice, 1, "From", region);
            int to = ResolveAnchor(slice, 2, "To", region);
            if (from > to)
                throw new InvalidDataException($"Reversed slice '{id}' in '{target.FullName}'.");
            region = new(from, to);
        }

        int ResolveAnchor(CustomAttribute slice, int index, string prefix, Region bounds)
        {
            At point = (At)(int)slice.ConstructorArguments[index].Value;
            var anchors = declaration.Selectors.Sites(instructions, point,
                Option<string?>(slice, prefix + "Target", null), Option(slice, prefix + "Ordinal", -1), bounds.Start, bounds.End).ToArray();
            if (anchors.Length != 1)
                throw new InvalidDataException($"Slice {prefix.ToLowerInvariant()} anchor resolved to {anchors.Length} instructions in '{target.FullName}'.");
            return anchors[0];
        }

        string kind = attribute.AttributeType.Name;
        At point = kind switch
        {
            "ModifyArgAttribute" => At.INVOKE,
            "ModifyConstantAttribute" => At.CONSTANT,
            _ => (At)(int)attribute.ConstructorArguments[1].Value
        };
        string? selector = kind == "ModifyConstantAttribute" ? Text(attribute, 1) : Option<string?>(attribute, "Target", null);
        AtShift shift = Option(attribute, "Shift", AtShift.NONE);
        if (kind != "InjectAttribute" && shift != AtShift.NONE)
            throw new InvalidDataException($"{kind} requires an unshifted instruction.");
        if (kind == "RedirectAttribute" && point is not (At.INVOKE or At.FIELD or At.NEW))
            throw new InvalidDataException("Redirect requires INVOKE, FIELD or NEW.");
        if (!Enum.IsDefined(shift))
            throw new InvalidDataException($"Unknown shift {shift}.");

        int require = Option(attribute, "Require", 1);
        int allow = Option(attribute, "Allow", -1);
        int expect = Option(attribute, "Expect", 1);
        if (require < 0 || allow < -1 || expect < 0 || allow >= 0 && allow < require)
            throw new InvalidDataException("Invalid Require, Allow or Expect.");
        int[] matches = declaration.Selectors.Sites(instructions, point, selector,
            Option(attribute, "Ordinal", -1), region.Start, region.End).ToArray();
        if (matches.Length < require || allow >= 0 && matches.Length > allow || debug && matches.Length < expect)
            throw new InvalidDataException($"{kind} on '{target.FullName}' at {point}"
                + (selector == null ? "" : $" {declaration.Selectors.Describe(selector)}")
                + $" selected {matches.Length} site(s) (Require {require}, Allow {allow}, Expect {expect}).");

        foreach (int match in matches)
        {
            int index = shift == AtShift.BY ? checked(match + Option(attribute, "By", 0)) : match;
            bool after = shift == AtShift.AFTER;
            if (index < region.Start || index > region.End)
                throw new InvalidDataException($"Shift leaves the selected region in '{target.FullName}'.");
            Instruction anchor = instructions[index];
            if (anchor.OpCode.OpCodeType == OpCodeType.Prefix || index > 0 && instructions[index - 1].OpCode.OpCodeType == OpCodeType.Prefix)
                throw new InvalidDataException($"Cannot split an IL prefix in '{target.FullName}'.");
            if (after && (anchor.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return or FlowControl.Throw
                || index == instructions.Length - 1))
                throw new InvalidDataException($"Cannot shift after '{anchor.OpCode}' in '{target.FullName}'.");

            if (debug)
            {
                string locals = string.Join(", ", target.Body.Variables.Select(local => $"{local.Index}={local.VariableType.FullName}"));
                Console.Error.WriteLine($"{declaration.Name}: {target.FullName} IL_{anchor.Offset:x4}; locals: {locals}");
            }
            yield return new Site(injection, index, point, after, point == At.HEAD && index == start && !after);
        }
    }

    private static int ConstructorStart(MethodDefinition method, Instruction[] instructions)
    {
        if (!method.IsConstructor || method.IsStatic)
            return 0;
        if (method.Body.HasExceptionHandlers)
            throw new InvalidDataException($"Cannot verify initialization chain in '{method.FullName}' with exception handlers.");

        // A linear, empty-stack prefix establishes that the first receiver is the uninitialized instance.
        int depth = 0;
        bool receiver = false;
        for (int index = 0; index < instructions.Length - 1; index++)
        {
            Instruction instruction = instructions[index];
            if (instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return or FlowControl.Throw)
                break;
            if (depth == 0)
                receiver = instruction.OpCode.Code == Code.Ldarg_0;
            if (receiver && instruction.OpCode.Code == Code.Call && instruction.Operand is MethodReference called
                && called.Name == ".ctor" && called.HasThis && depth == called.Parameters.Count + 1
                && (Same(called.DeclaringType, method.DeclaringType) || Same(called.DeclaringType, method.DeclaringType.BaseType)))
                return index + 1;

            int pops = PopCount(method, instruction, depth);
            if (pops >= depth && depth > 0)
                receiver = false;
            depth = depth - pops + PushCount(instruction);
        }
        throw new InvalidDataException($"Cannot verify base or this constructor call in '{method.FullName}'.");
    }

    private static void Add(List<Instruction> code, OpCode opcode, object? operand = null)
    {
        Instruction instruction = Instruction.Create(OpCodes.Nop);
        instruction.OpCode = opcode;
        instruction.Operand = operand;
        code.Add(instruction);
    }

    private static VariableDefinition Local(MethodDefinition method, TypeReference type)
    {
        var local = new VariableDefinition(type);
        method.Body.Variables.Add(local);
        return local;
    }

    private void EmitCallback(Site site, Instruction anchor, List<Instruction> code, int? depth)
    {
        var (_, handler, target, attribute, _, _) = site.Injection;
        MethodDefinition source = site.Injection.Declaration.Source.Methods.Single(item => methods.TryGetValue(item, out var copy) && copy == handler);
        bool returns = target.ReturnType.MetadataType != MetadataType.Void;
        bool cancellable = Option(attribute, "Cancellable", false);
        bool atReturn = !site.After && anchor.OpCode.Code == Code.Ret;
        if (handler.ReturnType.MetadataType != MetadataType.Void || !handler.HasParameters || handler.HasThis && !target.HasThis)
            throw new InvalidDataException($"Invalid callback signature '{handler.FullName}'.");
        if (cancellable && (target.IsConstructor || target.Body.HasExceptionHandlers || depth == null))
            throw new InvalidDataException($"Cannot safely cancel '{target.FullName}' at this instruction.");

        TypeReference callbackBase = game.ImportReference(typeof(CallbackInfo));
        TypeReference callbackType = callbackBase;
        if (returns)
        {
            if (target.ReturnType.IsPointer || target.ReturnType.IsByReference || target.ReturnType.MetadataType == MetadataType.TypedByReference
                || target.ReturnType.IsValueType && target.ReturnType.Resolve()?.CustomAttributes.Any(item =>
                    item.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute") == true)
                throw new InvalidDataException($"Unsupported callback result type '{target.ReturnType.FullName}'.");
            var closed = new GenericInstanceType(game.ImportReference(typeof(CallbackInfoReturnable<>)));
            closed.GenericArguments.Add(target.ReturnType);
            callbackType = closed;
        }

        int callbackIndex = handler.Parameters.IndexOf(handler.Parameters.FirstOrDefault(parameter => Same(parameter.ParameterType, callbackType)));
        if (callbackIndex < 0)
            throw new InvalidDataException($"Callback '{source.Name}' needs a '{callbackType.FullName}' parameter.");
        int explicitArguments = callbackIndex;
        bool receiver = !handler.HasThis && target.HasThis && explicitArguments == target.Parameters.Count + 1
            && Same(handler.Parameters[0].ParameterType, target.DeclaringType);
        if (explicitArguments != 0 && explicitArguments != target.Parameters.Count + (receiver ? 1 : 0))
            throw new InvalidDataException($"Callback '{source.Name}' must take all target arguments or none.");

        VariableDefinition callback = Local(target, callbackType);
        VariableDefinition? value = null;
        if (returns)
        {
            value = Local(target, target.ReturnType);
            if (atReturn)
                Add(code, OpCodes.Stloc, value);
            else
            {
                Add(code, OpCodes.Ldloca, value);
                Add(code, OpCodes.Initobj, target.ReturnType);
            }
        }

        Add(code, OpCodes.Ldstr, target.Name);
        Add(code, cancellable ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        var constructor = new MethodReference(".ctor", game.TypeSystem.Void, callbackType) { HasThis = true };
        constructor.Parameters.Add(new ParameterDefinition(game.TypeSystem.String));
        constructor.Parameters.Add(new ParameterDefinition(game.TypeSystem.Boolean));
        GenericParameter? resultParameter = null;
        if (returns)
        {
            resultParameter = ((GenericInstanceType)callbackType).ElementType.GenericParameters[0];
            constructor.Parameters.Add(new ParameterDefinition(resultParameter));
            Add(code, OpCodes.Ldloc, value);
        }
        Add(code, OpCodes.Newobj, constructor);
        Add(code, OpCodes.Stloc, callback);

        if (handler.HasThis || receiver)
            Add(code, OpCodes.Ldarg_0);
        for (int argument = receiver ? 1 : 0; argument < explicitArguments; argument++)
        {
            ParameterDefinition parameter = target.Parameters[argument - (receiver ? 1 : 0)];
            if (!Same(parameter.ParameterType, handler.Parameters[argument].ParameterType))
                throw new InvalidDataException($"Callback '{source.Name}' argument '{parameter.Name}' has the wrong type.");
            Add(code, OpCodes.Ldarg, parameter);
        }
        Add(code, OpCodes.Ldloc, callback);

        for (int index = callbackIndex + 1; index < handler.Parameters.Count; index++)
        {
            var capture = Attribute(source.Parameters[index], "CaptureLocal")
                ?? throw new InvalidDataException($"Callback parameter '{source.Parameters[index].Name}' needs CaptureLocal.");
            int slot = (int)capture.ConstructorArguments[0].Value;
            int originalLocals = originalLocalCounts[target];
            if (!target.Body.InitLocals || slot < 0 || slot >= originalLocals ||
                !Same(handler.Parameters[index].ParameterType, target.Body.Variables[slot].VariableType))
                throw new InvalidDataException($"Local slot {slot} is not initialized or does not match callback '{source.Name}'.");
            Add(code, OpCodes.Ldloc, target.Body.Variables[slot]);
        }

        Add(code, OpCodes.Call, handler);
        if (cancellable)
        {
            Instruction resume = Instruction.Create(OpCodes.Nop);
            Add(code, OpCodes.Ldloc, callback);
            Add(code, OpCodes.Call, game.ImportReference(typeof(CallbackInfo).GetProperty(nameof(CallbackInfo.IsCancelled))!.GetMethod!));
            Add(code, OpCodes.Brfalse, resume);
            for (int index = atReturn && returns ? 1 : 0; index < depth; index++)
                Add(code, OpCodes.Pop);
            if (returns)
            {
                Add(code, OpCodes.Ldloc, callback);
                Add(code, OpCodes.Call, new MethodReference("get_ReturnValue", resultParameter!, callbackType) { HasThis = true });
            }
            Add(code, OpCodes.Ret);
            code.Add(resume);
        }

        if (atReturn && returns)
            Add(code, OpCodes.Ldloc, value);
    }

    private readonly Dictionary<MethodDefinition, int> originalLocalCounts = new();

    private static void Insert(MethodBody body, Instruction anchor, List<Instruction> code, bool after, bool entry)
    {
        if (code.Count == 0)
            return;

        ILProcessor il = body.GetILProcessor();
        if (after)
        {
            foreach (Instruction item in code)
            {
                il.InsertAfter(anchor, item);
                anchor = item;
            }
            return;
        }

        if (!entry)
        {
            foreach (Instruction instruction in body.Instructions)
            {
                if (instruction.Operand == anchor)
                    instruction.Operand = code[0];
                else if (instruction.Operand is Instruction[] branches)
                    for (int index = 0; index < branches.Length; index++)
                        if (branches[index] == anchor)
                            branches[index] = code[0];
            }
            foreach (ExceptionHandler handler in body.ExceptionHandlers)
            {
                if (handler.TryStart == anchor) handler.TryStart = code[0];
                if (handler.TryEnd == anchor) handler.TryEnd = code[0];
                if (handler.HandlerStart == anchor) handler.HandlerStart = code[0];
                if (handler.HandlerEnd == anchor) handler.HandlerEnd = code[0];
                if (handler.FilterStart == anchor) handler.FilterStart = code[0];
            }
        }
        foreach (Instruction item in code)
            il.InsertBefore(anchor, item);
    }
}
