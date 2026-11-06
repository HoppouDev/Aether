using Aether.Modding;

public sealed class DependencyInit : IModInitializer
{
    public void Initialize() => System.Console.WriteLine("DEPENDENCY");
}
