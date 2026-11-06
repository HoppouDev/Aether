using Aether.Modding;
using Aether.Tests;

[Mixin("LegacyTarget")]
[Slice("local-call", At.HEAD, At.TAIL)]
public class TargetMixin
{
#pragma warning disable CS0649 // The weaver binds this declaration to the target field.
    [Shadow("oldValue")] private int value;
#pragma warning restore CS0649
    [Unique] private int state;

    [Shadow("Hidden(System.Int32)")]
    private int Original(int n) => throw new NotSupportedException();

    [Inject(".ctor()", At.RETURN)]
    private void Init(CallbackInfo ci) => state = value + 20;

    [Inject(".ctor(System.Int32)", At.HEAD)]
    private void Seed(int seed, CallbackInfo ci) => state = seed + 100;

    [Inject("Entry(System.Int32)", At.HEAD, Cancellable = true)]
    private static void Entry(int n, CallbackInfoReturnable<int> cir)
    {
        if (n == 7)
        {
            using var reader = new StreamReader(ModResources.OpenRead("test", "nested/value.txt"));
            cir.SetReturnValue(int.Parse(reader.ReadToEnd()));
        }
    }

    [Inject("Ret", At.RETURN, Cancellable = true)]
    private static void Return(CallbackInfoReturnable<int> cir) => cir.SetReturnValue(cir.ReturnValue + 20);

    [ModifyArg("Calls", Target = "Target::Hidden(System.Int32)", Index = 0)]
    private static int Arg(int n) => n + 1;

    [Redirect("Calls", At.INVOKE, Target = "Target::Hidden(System.Int32)")]
    private int Call(Target self, int n) => n * 2 + value;

    [ModifyConstant("Const", "int:12")]
    private static int Constant(int n) => Numbers.Increase(n);

    [Overwrite("Replace")]
    private int Replacement() => Original(value * 2);

    [Inject("Local", At.INVOKE, Target = "Target::Hidden(System.Int32)", Slice = "local-call")]
    private void Capture(CallbackInfoReturnable<int> cir, [CaptureLocal(0)] int x) => state = x;

    [Inject("Guard", At.INVOKE, Target = "Target::Hidden(System.Int32)", Shift = AtShift.AFTER)]
    private void After(CallbackInfoReturnable<int> cir) => state += 1;

    [Redirect("Read", At.FIELD, Target = "Target::value:System.Int32")]
    private int ReadField(Target self) => value + 7;

    [Redirect("Make", At.NEW, Target = "Target::.ctor()")]
    private static Target MakeNew()
    {
        var target = new Target();
        ((ITargetAccess)(object)target).Set(77);
        return target;
    }

    [Overwrite("State")]
    private int GetState() => state;
}

[Mixin(typeof(Target))]
public interface ITargetAccess
{
    [Accessor("value")] int Get();
    [Accessor("value")] void Set(int value);
    [Invoker("Hidden(System.Int32)")] int Hidden(int n);
}

[Mixin(typeof(Target))]
public interface ITargetFactory
{
    [Invoker(".ctor()")] Target Create();
}

public class Init : IModInitializer
{
    public void Initialize()
    {
        using var resource = new StreamReader(ModResources.OpenRead("test", "nested/value.txt"));
        Check(int.Parse(resource.ReadToEnd()), 50);
        try
        {
            using var outside = ModResources.OpenRead("test", "../aether.mod.json");
            throw new InvalidOperationException("Resource lookup allowed traversal.");
        }
        catch (InvalidDataException) { }
        var target = new Target();
        var access = (ITargetAccess)(object)target;
        Check(target.State(), 23);
        Check(new Target(5).State(), 105);
        Check(target.Entry(7), 50);
        Check(target.Entry(5), 6);
        Check(target.Ret(9), 29);
        Check(target.Ret(0), 19);
        Check(target.Calls(5), 15);
        Check(target.Const(), 14);
        Check(target.Replace(), 9);
        Check(target.Local(5), 13);
        Check(target.State(), 10);
        Check(target.Guard(5), 8);
        Check(target.State(), 11);
        Check(target.Read(), 10);
        Check(target.Make(), 77);
        Check(access.Get(), 3);
        Check(access.Hidden(5), 8);
        access.Set(6);
        Check(access.Get(), 6);
        var create = MixinAccess.BindStatic<Func<Target>>(typeof(Target), typeof(ITargetFactory), "Create");
        Check(create().State(), 23);
        Console.WriteLine("MOD OK");
    }
    private static void Check(int result, int expected, [System.Runtime.CompilerServices.CallerArgumentExpression("result")] string? expression = null)
    {
        if (result != expected) throw new Exception($"{expression}: got {result}, expected {expected}");
    }
}

[Mixin(typeof(Target))]
public static class InvalidMixin
{
    [Inject("Missing", At.HEAD)]
    private static void Missing(CallbackInfo callback) { }
}

[Mixin(typeof(Target))]
public static class ConflictingMixin
{
    [Redirect("Calls", At.INVOKE, Target = "Target::Hidden(System.Int32)")]
    private static int Replace(Target target, int value) => value;
}
