using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Aether.Modding;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class EditILAttribute(string method) : Attribute
{
    public string Method { get; } = method;
    public int Order { get; set; } = 1000;
}

public sealed class ILPatchContext
{
    internal ILPatchContext(MethodDefinition method) => Method = method;

    public MethodDefinition Method { get; }
    public MethodBody Body => Method.Body;
    public ILProcessor IL => Body.GetILProcessor();
}
