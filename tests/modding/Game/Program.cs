using System;
using System.Runtime.InteropServices;
using Aether.Modding;
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
}
public static class Program
{
    [DllImport("aether_fixture_native")]
    private static extern int FixtureValue();

    public static void Main(string[] args)
    {
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
