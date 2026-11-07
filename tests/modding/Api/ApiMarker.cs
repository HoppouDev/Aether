namespace Aether.Api;

public static class ApiMarker
{
    private static int sequence;

    public static int Next() => ++sequence;
}
