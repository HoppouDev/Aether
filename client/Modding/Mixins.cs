namespace Aether.Modding;

public enum At { HEAD, RETURN, TAIL, INVOKE, FIELD, CONSTANT, NEW }
public enum AtShift { NONE, BEFORE, AFTER, BY }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
public sealed class MixinAttribute : Attribute
{
    public MixinAttribute(Type target) { }
    public MixinAttribute(string target) { }
    public int Priority { get; set; } = 1000;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Method)]
public sealed class UniqueAttribute : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Method)]
public sealed class ShadowAttribute(string? target = null) : Attribute
{
    public string? Target { get; } = target;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OverwriteAttribute(string? target = null) : Attribute
{
    public string? Target { get; } = target;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class WrapMethodAttribute(string method) : Attribute
{
    public string Method { get; } = method;
    public int Order { get; set; } = 1000;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class AccessorAttribute(string target) : Attribute
{
    public string Target { get; } = target;
    public bool IsStatic { get; set; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class InvokerAttribute(string target) : Attribute
{
    public string Target { get; } = target;
    public bool IsStatic { get; set; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true)]
public sealed class SliceAttribute(string id, At from, At to) : Attribute
{
    public string Id { get; } = id;
    public At From { get; } = from;
    public At To { get; } = to;
    public string? FromTarget { get; set; }
    public string? ToTarget { get; set; }
    public int FromOrdinal { get; set; } = -1;
    public int ToOrdinal { get; set; } = -1;
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class CaptureLocalAttribute(int index) : Attribute
{
    public int Index { get; } = index;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public abstract class InjectorAttribute(string method, At at) : Attribute
{
    public string Method { get; } = method;
    public At At { get; } = at;
    public string? Target { get; set; }
    public int Ordinal { get; set; } = -1;
    public string? Slice { get; set; }
    public AtShift Shift { get; set; }
    public int By { get; set; }
    public int Require { get; set; } = 1;
    public int Allow { get; set; } = -1;
    public int Expect { get; set; } = 1;
    public int Order { get; set; } = 1000;
}

public sealed class InjectAttribute(string method, At at) : InjectorAttribute(method, at)
{
    public bool Cancellable { get; set; }
}

public sealed class RedirectAttribute : InjectorAttribute
{
    public RedirectAttribute(string method, At at) : base(method, at) => Order = 10000;
}

public sealed class WrapOperationAttribute(string method, At at) : InjectorAttribute(method, at);

public sealed class ModifyArgAttribute(string method) : InjectorAttribute(method, At.INVOKE)
{
    public int Index { get; set; } = -1;
}

public sealed class ModifyConstantAttribute(string method, string literal) : InjectorAttribute(method, At.CONSTANT)
{
    public string Literal { get; } = literal;
}

public class CallbackInfo(string id, bool cancellable)
{
    public string Id { get; } = id;
    public bool IsCancellable { get; } = cancellable;
    public bool IsCancelled { get; private set; }

    public void Cancel()
    {
        if (!IsCancellable)
            throw new InvalidOperationException($"Callback '{Id}' is not cancellable.");
        IsCancelled = true;
    }
}

public sealed class CallbackInfoReturnable<T>(string id, bool cancellable, T value) : CallbackInfo(id, cancellable)
{
    public T ReturnValue { get; private set; } = value;

    public void SetReturnValue(T value)
    {
        Cancel();
        ReturnValue = value;
    }
}

public static class MixinAccess
{
    public static TDelegate BindStatic<TDelegate>(Type target, Type mixin, string name) where TDelegate : Delegate
    {
        var signature = typeof(TDelegate).GetMethod("Invoke")!;
        var bridge = target.GetMethod(
            $"__aether$access${mixin.FullName}${name}",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly,
            null,
            signature.GetParameters().Select(parameter => parameter.ParameterType).ToArray(),
            null);
        return (bridge ?? throw new MissingMethodException(target.FullName, name)).CreateDelegate<TDelegate>();
    }
}
