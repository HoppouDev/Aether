namespace Aether.Examples;

public static class StartupLog
{
    public static void Write(string message) => System.Console.WriteLine($"[startup-example] {message}");
}
