namespace Aether.Tests;

public static class Counter
{
    private static int sequence;

    public static int Next() => ++sequence;
}
