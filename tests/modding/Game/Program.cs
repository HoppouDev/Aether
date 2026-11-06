using System;
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
    public static void Main(string[] args)
    {
        ClientStartup.InitializeMods?.Invoke();
        Console.WriteLine("GAME");
    }
}
