using System.Reflection;
using System.Runtime.Loader;
using Aether.Modding;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Aether.Bootstrap;

internal sealed partial class AssemblyWeaver
{
    private sealed class EditorContext(ModPackage package) : AssemblyLoadContext($"Aether IL editor {package.Manifest.Id}", isCollectible: true)
    {
        private readonly AssemblyDependencyResolver resolver = new(package.EntryPath);

        internal Assembly LoadEditor(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return LoadFromStream(stream);
        }

        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name is "Aether.Modding" or "Mono.Cecil")
                return Default.LoadFromAssemblyName(name);
            if (name.Name is "Aether.Api" || name.Name != null && Program.SharedNames.Contains(name.Name))
                throw new InvalidOperationException($"'{name.Name}' is unavailable to startup IL editors.");
            string path = resolver.ResolveAssemblyToPath(name) ?? Path.Combine(Path.GetDirectoryName(package.EntryPath)!, name.Name + ".dll");
            return File.Exists(path) ? LoadEditor(path) : null;
        }
    }

    private void ApplyILEditors()
    {
        var assemblies = new Dictionary<ModPackage, (EditorContext Context, Assembly Assembly)>();
        try
        {
            foreach (Injection editor in Ordered(ilEditors))
            {
                WithContext(editor.Declaration, () =>
                {
                    MethodDefinition target = editor.Target;
                    try
                    {
                        ModPackage package = editor.Declaration.Package;
                        if (!assemblies.TryGetValue(package, out var loaded))
                        {
                            var context = new EditorContext(package);
                            try { loaded = (context, context.LoadEditor(package.EntryPath)); }
                            catch { context.Unload(); throw; }
                            assemblies.Add(package, loaded);
                        }

                        var method = (MethodInfo)loaded.Assembly.ManifestModule.ResolveMethod(editor.Handler.MetadataToken.ToInt32())!;
                        EditBody(target, (Action<ILPatchContext>)method.CreateDelegate(typeof(Action<ILPatchContext>)));
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidDataException($"IL editor '{editor.Handler.FullName}' failed for '{target.FullName}': {exception.Message}", exception);
                    }
                });
            }
        }
        finally
        {
            foreach (var (context, _) in assemblies.Values)
                context.Unload();
        }
    }

    private void EditBody(MethodDefinition target, Action<ILPatchContext> edit)
    {
        var oldReferences = game.AssemblyReferences.Select(reference => reference.FullName).ToHashSet(StringComparer.Ordinal);
        var oldOperands = target.Body.Instructions.ToDictionary(instruction => instruction, instruction => Snapshot(instruction.Operand));
        var oldLocals = target.Body.Variables.ToDictionary(local => local, local => Snapshot(local.VariableType));
        var oldCatches = target.Body.ExceptionHandlers.ToDictionary(handler => handler, handler => Snapshot(handler.CatchType));
        edit(new ILPatchContext(target));

        foreach (AssemblyNameReference reference in game.AssemblyReferences.Where(reference => !oldReferences.Contains(reference.FullName)))
            TrackAssembly(reference);
        foreach (VariableDefinition local in target.Body.Variables)
            if (oldLocals.GetValueOrDefault(local) != Snapshot(local.VariableType))
                TrackType(local.VariableType);
        foreach (ExceptionHandler handler in target.Body.ExceptionHandlers)
            if (handler.CatchType != null && oldCatches.GetValueOrDefault(handler) != Snapshot(handler.CatchType))
                TrackType(handler.CatchType);

        Instruction[] instructions = target.Body.Instructions.ToArray();
        var indexes = instructions.Select((instruction, index) => (instruction, index))
            .ToDictionary(item => item.instruction, item => item.index);
        foreach (Instruction instruction in instructions)
        {
            instruction.OpCode = LongBranch(instruction.OpCode);
            ValidateOperand(target, instruction);
            switch (instruction.Operand)
            {
                case Instruction branch when !indexes.ContainsKey(branch):
                case Instruction[] branches when branches.Any(branch => !indexes.ContainsKey(branch)):
                    throw new InvalidDataException("An IL branch points outside the edited body.");
                case VariableDefinition local when !target.Body.Variables.Contains(local):
                    throw new InvalidDataException("An IL instruction refers to a local outside the edited body.");
                case ParameterDefinition parameter when !target.Parameters.Contains(parameter) &&
                    (!target.HasThis || parameter != target.Body.ThisParameter):
                    throw new InvalidDataException("An IL instruction refers to a parameter outside the edited method.");
            }
            if (oldOperands.GetValueOrDefault(instruction) != Snapshot(instruction.Operand))
                foreach (TypeReference type in ReferencedTypes(instruction.Operand))
                    TrackType(type);
        }
        bool ValidRange(Instruction? start, Instruction? end) => start != null && indexes.TryGetValue(start, out int first) &&
            (end == null || indexes.TryGetValue(end, out int last) && first < last);
        if (instructions.Length == 0 || target.Body.ExceptionHandlers.Any(handler =>
            !ValidRange(handler.TryStart, handler.TryEnd) || !ValidRange(handler.HandlerStart, handler.HandlerEnd) ||
            handler.HandlerType == ExceptionHandlerType.Filter && !ValidRange(handler.FilterStart, handler.HandlerStart)))
            throw new InvalidDataException("The edited body is empty or contains an invalid exception region.");

        AnalyzeDepths(target, instructions, verifyBody: true);
    }

    private static (object? Operand, string Signature) Snapshot(object? operand) =>
        (operand, string.Join("|", ReferencedTypes(operand).Select(type =>
            type.FullName + ":" + string.Join(",", ReferencedAssemblies(type).Select(reference => reference.FullName)))));

    private static void ValidateOperand(MethodDefinition target, Instruction instruction)
    {
        object? operand = instruction.Operand;
        bool valid = instruction.OpCode.OperandType switch
        {
            OperandType.InlineNone => operand == null,
            OperandType.InlineBrTarget or OperandType.ShortInlineBrTarget => operand is Instruction,
            OperandType.InlineField => operand is FieldReference,
            OperandType.InlineI => operand is int,
            OperandType.InlineI8 => operand is long,
            OperandType.InlineMethod => operand is MethodReference,
            OperandType.InlineR => operand is double,
            OperandType.InlineSig => operand is CallSite,
            OperandType.InlineString => operand is string,
            OperandType.InlineSwitch => operand is Instruction[] targets && targets.All(branch => branch != null),
            OperandType.InlineTok => operand is TypeReference or MethodReference or FieldReference,
            OperandType.InlineType => operand is TypeReference,
            OperandType.InlineVar or OperandType.ShortInlineVar => operand is VariableDefinition,
            OperandType.InlineArg or OperandType.ShortInlineArg => operand is ParameterDefinition,
            OperandType.ShortInlineI => instruction.OpCode.Code == Code.Ldc_I4_S ? operand is sbyte : operand is byte,
            OperandType.ShortInlineR => operand is float,
            _ => false
        };
        if (!valid)
            throw new InvalidDataException($"Invalid operand for '{instruction.OpCode}'.");

        int slot = instruction.OpCode.Code switch
        {
            >= Code.Ldloc_0 and <= Code.Ldloc_3 => instruction.OpCode.Code - Code.Ldloc_0,
            >= Code.Stloc_0 and <= Code.Stloc_3 => instruction.OpCode.Code - Code.Stloc_0,
            _ => -1
        };
        if (slot >= target.Body.Variables.Count)
            throw new InvalidDataException($"'{instruction.OpCode}' refers to a local outside the edited body.");
        if (instruction.OpCode.Code is >= Code.Ldarg_0 and <= Code.Ldarg_3 &&
            instruction.OpCode.Code - Code.Ldarg_0 >= target.Parameters.Count + (target.HasThis ? 1 : 0))
            throw new InvalidDataException($"'{instruction.OpCode}' refers to a parameter outside the edited method.");
    }

}
