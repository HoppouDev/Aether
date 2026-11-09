using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Aether.Modding;

public struct FieldSample
{
    public int Value;
}

public class Target
{
    private int value;
    private int state;
    public Target() { value = 3; }
    public Target(int seed) : this() { state = seed; }
    private int Hidden(int n) => n + value;
    public int Entry(int n) => n + 1;
    public int Ret(int n) => n > 0 ? n : -1;
    public int Calls(int n) => Hidden(n);
    public int Const() => 12;
    public int Replace() => 1;
    public int Local(int n) { int x = n + 5; return Hidden(x); }
    public int Guard(int n) { try { return Hidden(n); } finally { Console.WriteLine("FINALLY"); } }
    public int Make() => new Target().value;
    public int Read() => value;
    public int State() => state;
    public int Wrapped(int n) { try { return n < 0 ? -1 : Hidden(n); } finally { Console.WriteLine("WRAPPED FINALLY"); } }
    public int WrapField() => value;
    public void WrapWrite(int n) => value = n;
    public Target WrapNew(int n) => new(n);
    public static T Echo<T>(T item) where T : class => item;
    public T OpenCall<T>(T item) where T : class => Echo(item);
    public string ClosedCall(string item) => Echo(item);
    public string ModifyGenericArg(string item) => Echo(item);
    public static string StaticCall(string item) => Echo(item);
    public ref int SelectRef(ref int amount, out int snapshot) { snapshot = amount; return ref amount; }
    public ref readonly int ReadonlyRef(in int amount) => ref amount;
    public int RefCall(ref int amount) => SelectRef(ref amount, out _);
    public int Rewrite(int amount) => amount + 2;
    public int Recurse(int depth) => depth == 0 ? 1 : Recurse(depth - 1) + 1;
    private static FieldSample MakeFieldSample(int value) => new() { Value = value };
    public int ReadStructValue(int value) => MakeFieldSample(value).Value;
    public int ReadStructAddress(ref FieldSample sample) => sample.Value;
    public void WriteStructAddress(ref FieldSample sample, int value) => sample.Value = Echo(value.ToString()).Length;
    public string ThrowWithPendingOperand(string left, string right) => left + (right ?? throw new ArgumentNullException(nameof(right)));
    public static int WriteEmbeddedField(int value)
    {
        Aether.Tests.Embedded.Fields.Value = value;
        return Aether.Tests.Embedded.Fields.Value;
    }
}
public static class Program
{
    [DllImport("aether_fixture_native")]
    private static extern int FixtureValue();

    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, resolve) =>
        {
            if (new AssemblyName(resolve.Name).Name != "Aether.Tests.Embedded")
                return null;
            using var stream = typeof(Program).Assembly.GetManifestResourceStream("Terraria.Libraries.Aether.Tests.Embedded.dll");
            return AssemblyLoadContext.Default.LoadFromStream(stream);
        };
        Aether.NativeLibraries.Install();
        if (Array.IndexOf(args, "-testnative") >= 0)
            Console.WriteLine($"NATIVE {FixtureValue()}");
        ClientStartup.InitializeMods?.Invoke();
        if (Array.IndexOf(args, "-testconnection") >= 0)
            Console.WriteLine(ClientContent.ConnectionVersion ?? "VANILLA");
        if (Array.IndexOf(args, "-testapi") >= 0)
            new Target().Entry(0);
        Console.WriteLine("GAME");
    }
}
