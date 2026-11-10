using Aether.Modding;
using Mono.Cecil;
using Mono.Cecil.Cil;

public delegate ref int SelectRefNext(ref int amount, out int snapshot);
public delegate ref int SelectRefOperation(Target self, ref int amount, out int snapshot);
public delegate ref readonly int ReadonlyNext(in int amount);
public delegate int ReadStructNext(ref FieldSample receiver);
public delegate void WriteStructNext(ref FieldSample receiver, int value);
public delegate int VirtualInputNext(in int value);
public delegate int VirtualInputOperation(Target receiver, in int value);

[Mixin(typeof(Target))]
public class WrapperPatches
{
    [Inject("Wrapped", At.RETURN)]
    private static void SeeResult(CallbackInfoReturnable<int> info) => Console.WriteLine($"BODY {info.ReturnValue}");

    [WrapMethod("Wrapped", Order = 500)]
    private int Outer(Func<int, int> next, int amount) => amount == 0 ? 42 : next(amount + 1) * 2;

    [WrapMethod("Wrapped")]
    private static int Inner(Func<int, int> next, int amount) => next(amount * 2) + 3;

    [ModifyArg("Wrapped", Target = "Target::Hidden(System.Int32)")]
    private static int Increase(int amount) => amount + 1;

    [WrapOperation("Wrapped", At.INVOKE, Target = "Target::Hidden(System.Int32)")]
    private int Call(Func<Target, int, int> next, Target self, int amount) => next(self, amount + 2) * 2;

    [WrapOperation("Wrapped", At.INVOKE, Target = "Target::Hidden(System.Int32)", Order = 2000)]
    private static int CallInner(Func<Target, int, int> next, Target self, int amount) => next(self, amount) + 1;

    [WrapOperation("WrapField", At.FIELD, Target = "Target::value:System.Int32")]
    private static int Field(Func<Target, int> next, Target self) => next(self) + 1;

    [WrapOperation("WrapWrite", At.FIELD, Target = "Target::value:System.Int32")]
    private static void Write(Action<Target, int> next, Target self, int amount) => next(self, amount * 2);

    [WrapOperation("WrapNew", At.NEW, Target = "Target::.ctor(System.Int32)")]
    private static Target New(Func<int, Target> next, int amount) => next(amount * 2);

    [WrapMethod("Echo`1(T)")]
    private static TValue Echo<TValue>(Func<TValue, TValue> next, TValue item) where TValue : class => next(item);

    [WrapOperation("OpenCall`1(T)", At.INVOKE, Target = "Target::Echo<T>(T)")]
    private static TValue Open<TValue>(Func<TValue, TValue> next, TValue item) where TValue : class
    {
        Console.WriteLine("OPEN CALL");
        return next(item);
    }

    [WrapOperation("ClosedCall", At.INVOKE, Target = "Target::Echo<System.String>(System.String)")]
    private static string Closed(Func<string, string> next, string item) => next(item + " wrapped");

    [ModifyArg("ModifyGenericArg", Target = "Target::Echo<System.String>(System.String)")]
    private static string ModifyGenericArg(string item) => item + " modified";

    [WrapMethod("SelectRef(System.Int32&,System.Int32&)")]
    private ref int Select(SelectRefNext next, ref int amount, out int snapshot)
    {
        amount++;
        return ref next(ref amount, out snapshot);
    }

    [WrapMethod("ReadonlyRef")]
    private ref readonly int ReadOnly(ReadonlyNext next, in int amount) => ref next(in amount);

    [Inject("VirtualInput(System.Int32&)", At.RETURN, Cancellable = true)]
    private static void ObserveVirtualInput(in int amount, CallbackInfoReturnable<int> info) => info.SetReturnValue(info.ReturnValue + amount);

    [WrapMethod("VirtualInput(System.Int32&)")]
    private static int WrapVirtualInput(VirtualInputNext next, in int amount) => next(in amount) + 2;

    [WrapOperation("CallVirtualInput", At.INVOKE, Target = "Target::VirtualInput(System.Int32&)")]
    private static int WrapVirtualOperation(VirtualInputOperation next, Target receiver, in int amount) => next(receiver, in amount) + 3;

    [WrapOperation("RefCall", At.INVOKE, Target = "Target::SelectRef(System.Int32&,System.Int32&)")]
    private static ref int RefCall(SelectRefOperation next, Target self, ref int amount, out int snapshot)
    {
        amount += 2;
        return ref next(self, ref amount, out snapshot);
    }

    [WrapMethod("Rewrite")]
    private static int WrapRewrite(Func<int, int> next, int amount) => next(amount) * 2;

    [WrapMethod("Recurse")]
    private static int Recurse(Func<int, int> next, int depth) => next(depth) + 1;
}

[Mixin(typeof(Target))]
public static class FieldReceiverPatches
{
    [WrapOperation("ReadStructValue", At.INVOKE, Target = "Target::MakeFieldSample(System.Int32)")]
    private static FieldSample Make(Func<int, FieldSample> next, int value) => next(value * 2);

    [WrapOperation("ReadStructValue", At.FIELD, Target = "FieldSample::Value:System.Int32")]
    private static int ReadValue(Func<FieldSample, int> next, FieldSample receiver) => next(receiver) + 1;

    [WrapOperation("ReadStructAddress", At.FIELD, Target = "FieldSample::Value:System.Int32")]
    private static int ReadAddress(ReadStructNext next, ref FieldSample receiver) => next(ref receiver) + 2;

    [WrapOperation("WriteStructAddress", At.FIELD, Target = "FieldSample::Value:System.Int32")]
    private static void WriteAddress(WriteStructNext next, ref FieldSample receiver, int value) => next(ref receiver, value * 3);

    [Redirect("ReadStructBranch", At.FIELD, Target = "FieldSample::Value:System.Int32")]
    private static int ReadBranch(ref FieldSample receiver) => receiver.Value + 2;

    [WrapOperation("WriteStructBranch", At.FIELD, Target = "FieldSample::Value:System.Int32")]
    private static void WriteBranch(WriteStructNext next, ref FieldSample receiver, int value) => next(ref receiver, value * 3);

    [WrapOperation("WriteEmbeddedField", At.FIELD, Target = "Aether.Tests.Embedded.Fields::Value:System.Int32", Ordinal = 0)]
    private static void WriteEmbedded(Action<int> next, int value) => next(value + 4);
}

[Mixin("Target")]
public static class StartupEditor
{
    [EditIL("Rewrite")]
    private static void Rewrite(ILPatchContext patch)
    {
        var result = new VariableDefinition(patch.Method.Module.TypeSystem.Int32);
        patch.Body.Variables.Add(result);
        var ret = patch.Body.Instructions.Last(instruction => instruction.OpCode == OpCodes.Ret);
        var loadResult = patch.IL.Create(OpCodes.Ldloc, result);
        patch.IL.InsertBefore(ret, patch.IL.Create(OpCodes.Stloc, result));
        patch.IL.InsertBefore(ret, patch.IL.Create(OpCodes.Br, loadResult));
        patch.IL.InsertBefore(ret, loadResult);
        patch.IL.InsertBefore(ret, patch.IL.Create(OpCodes.Ldc_I4, 5));
        patch.IL.InsertBefore(ret, patch.IL.Create(OpCodes.Mul));
        var increment = patch.Method.Module.ImportReference(typeof(EditorHelpers).GetMethod(nameof(EditorHelpers.Increment))!);
        patch.IL.InsertBefore(ret, patch.IL.Create(OpCodes.Call, increment));
    }

    [EditIL("ThrowWithPendingOperand")]
    private static void PreserveThrow(ILPatchContext patch) { }
}

[Mixin("Target")]
public static class EmbeddedResolverEditor
{
    [EditIL("WriteEmbeddedField")]
    private static void VerifyResolver(ILPatchContext patch)
    {
        var module = patch.Method.Module;
        var reference = module.AssemblyReferences.Single(name => name.Name == "Aether.Tests.Embedded");
        var embedded = module.AssemblyResolver.Resolve(reference);
        if (!ReferenceEquals(embedded.MainModule.AssemblyResolver, module.AssemblyResolver))
            throw new InvalidOperationException("Embedded DLL did not inherit the game resolver.");
    }
}

[Mixin("Target")]
public static class SameNameHelper
{
    private static int Rewrite(string text) => text.Length;
}

public static class EditorHelpers
{
    public static int Increment(int amount) => amount + 1;
}

[Mixin("Target")]
public static class InvalidEditor
{
    [EditIL("Rewrite(System.Int32)")]
    private static void Rewrite(ILPatchContext patch) => patch.IL.Emit(OpCodes.Br, patch.IL.Create(OpCodes.Nop));
}

[Mixin("Target")]
public static class InvalidOperandEditor
{
    [EditIL("Rewrite")]
    private static void Rewrite(ILPatchContext patch)
    {
        Instruction literal = patch.Body.Instructions.First(instruction => instruction.OpCode == OpCodes.Ldc_I4_2);
        literal.OpCode = OpCodes.Ldc_I4_S;
        literal.Operand = "not an integer";
    }
}

[Mixin("Target")]
public static class InvalidLocalEditor
{
    [EditIL("Rewrite")]
    private static void Rewrite(ILPatchContext patch) => patch.IL.InsertBefore(patch.Body.Instructions[0], patch.IL.Create(OpCodes.Ldloc_3));
}

[Mixin("Target")]
public static class WrongAssemblyWrapper
{
#pragma warning disable CS0436 // A type with the same name as the game's type, from this assembly.
    [WrapMethod("RoundTrip")]
    private static MixinTypeIdentity.Thing Wrap(Func<MixinTypeIdentity.Thing, MixinTypeIdentity.Thing> next, MixinTypeIdentity.Thing value) => next(value);
#pragma warning restore CS0436
}

namespace MixinTypeIdentity
{
    public sealed class Thing { }
}

[Mixin(typeof(Target))]
public static class InvalidWrapper
{
    [WrapMethod("Wrapped")]
    private static int Bad(Func<string, int> next, int amount) => next(amount.ToString());
}

[Mixin(typeof(Target))]
public class InvalidOperationWrapper
{
    [WrapOperation("StaticCall", At.INVOKE, Target = "Target::Echo<System.String>(System.String)")]
    private string Bad(Func<string, string> next, string item) => next(item);
}

public class WrapperTest : IModInitializer
{
    public void Initialize()
    {
        var target = new Target();
        Equal(42, target.Wrapped(0));
        Equal(58, target.Wrapped(2));
        Equal(4, target.WrapField());
        target.WrapWrite(5);
        Equal(11, target.WrapField());
        Equal(8, target.WrapNew(4).State());
        Equal("closed wrapped", target.ClosedCall("closed"));
        Equal("open", target.OpenCall("open"));
        Equal("arg modified", target.ModifyGenericArg("arg"));
        int amount = 5;
        ref int reference = ref target.SelectRef(ref amount, out int snapshot);
        Equal(6, snapshot);
        reference = 20;
        Equal(20, amount);
        Equal(23, target.RefCall(ref amount));
        ref readonly int readOnly = ref target.ReadonlyRef(in amount);
        amount = 24;
        Equal(24, readOnly);
        Equal(12, target.CallVirtualInput(3));
        Equal(42, target.Rewrite(2));
        Equal(6, target.Recurse(2));
        Console.WriteLine("WRAPPERS OK");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

public class ReceiverRegressionTest : IModInitializer
{
    public void Initialize()
    {
        var target = new Target();
        var sample = new FieldSample { Value = 7 };
        if (target.ReadStructValue(6) != 13 || target.ReadStructAddress(ref sample) != 9)
            throw new InvalidOperationException("Unexpected struct field read.");
        target.WriteStructAddress(ref sample, 1234);
        if (sample.Value != 12)
            throw new InvalidOperationException("Struct field write did not reach the original receiver.");
        var other = new FieldSample { Value = 30 };
        if (target.ReadStructBranch(false, ref sample, ref other) != 32)
            throw new InvalidOperationException("Struct field read lost the receiver at a branch join.");
        target.WriteStructBranch(true, ref sample);
        if (sample.Value != 15)
            throw new InvalidOperationException("Struct field write lost the receiver at a branch join.");
        if (Target.WriteEmbeddedField(6) != 10)
            throw new InvalidOperationException("Embedded field write did not execute the wrapper.");
        if (target.ThrowWithPendingOperand("left", "right") != "leftright")
            throw new InvalidOperationException("Edited method changed its return value.");
        try
        {
            target.ThrowWithPendingOperand("left", null!);
            throw new InvalidOperationException("Edited method failed to throw.");
        }
        catch (ArgumentNullException exception) when (exception.ParamName == "right") { }
        Console.WriteLine("REGRESSIONS OK");
    }
}
