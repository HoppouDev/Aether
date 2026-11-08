using System.Collections.Generic;

namespace Aether.Utilities;

/// <summary>Enum values' names (value.ToString(), which allocates every call), made once per value.</summary>
public static class EnumNames<TEnum> where TEnum : struct
{
    private static readonly Dictionary<TEnum, string> Names = new Dictionary<TEnum, string>();

    public static string Of(TEnum value)
    {
        lock (Names)
        {
            if (!Names.TryGetValue(value, out var name))
            {
                name = value.ToString();
                Names[value] = name;
            }
            return name;
        }
    }
}
