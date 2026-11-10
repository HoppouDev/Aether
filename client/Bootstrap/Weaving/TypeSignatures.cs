using Mono.Cecil;

namespace Aether.Bootstrap;

internal static class TypeSignatures
{
    internal static TypeReference NormalizeIn(TypeReference type) => type is RequiredModifierType
        { ElementType: ByReferenceType reference, ModifierType.FullName: "System.Runtime.InteropServices.InAttribute" }
        ? reference : type;

    internal static bool SameParameter(TypeReference left, ParameterAttributes leftFlags, TypeReference right, ParameterAttributes rightFlags)
    {
        left = NormalizeIn(left);
        right = NormalizeIn(right);
        const ParameterAttributes direction = ParameterAttributes.In | ParameterAttributes.Out;
        return Same(left, right) && (!left.IsByReference || (leftFlags & direction) == (rightFlags & direction));
    }

    internal static bool Same(TypeReference left, TypeReference right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left.MetadataType != right.MetadataType)
            return false;
        return (left, right) switch
        {
            (GenericParameter a, GenericParameter b) => a.Position == b.Position && a.Type == b.Type,
            (GenericInstanceType a, GenericInstanceType b) => Same(a.ElementType, b.ElementType) && SameTypes(a.GenericArguments, b.GenericArguments),
            (FunctionPointerType a, FunctionPointerType b) => a.CallingConvention == b.CallingConvention && a.HasThis == b.HasThis &&
                a.ExplicitThis == b.ExplicitThis && Same(a.ReturnType, b.ReturnType) && a.Parameters.Count == b.Parameters.Count &&
                a.Parameters.Zip(b.Parameters).All(pair => Same(pair.First.ParameterType, pair.Second.ParameterType)),
            (ArrayType a, ArrayType b) => a.IsVector == b.IsVector && a.Dimensions.SequenceEqual(b.Dimensions) && Same(a.ElementType, b.ElementType),
            (TypeSpecification a, TypeSpecification b) => Same(a.ElementType, b.ElementType) &&
                (a is not IModifierType modifier || b is IModifierType other && Same(modifier.ModifierType, other.ModifierType)),
            _ => left.FullName == right.FullName && (Scope(left) == Scope(right) || SameResolved(left, right))
        };
    }

    internal static bool SameTypes(IList<TypeReference> left, IList<TypeReference> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => Same(pair.First, pair.Second));

    private static string? Scope(TypeReference type) => type.Scope switch
    {
        AssemblyNameReference name => name.FullName,
        ModuleDefinition module => module.Assembly?.Name.FullName,
        _ => type.Scope?.Name
    };

    private static bool SameResolved(TypeReference left, TypeReference right)
    {
        try
        {
            return left.Resolve() is { } a && right.Resolve() is { } b &&
                a.FullName == b.FullName && a.Module.Assembly.Name.FullName == b.Module.Assembly.Name.FullName;
        }
        catch (AssemblyResolutionException) { return false; }
    }

    internal static FunctionPointerType MapFunctionPointer(FunctionPointerType source, Func<TypeReference, TypeReference> map)
    {
        var result = new FunctionPointerType
        {
            CallingConvention = source.CallingConvention, HasThis = source.HasThis, ExplicitThis = source.ExplicitThis,
            ReturnType = map(source.ReturnType)
        };
        foreach (ParameterDefinition parameter in source.Parameters)
            result.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, map(parameter.ParameterType)));
        return result;
    }
}
