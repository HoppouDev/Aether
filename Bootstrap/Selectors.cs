using System.Globalization;
using System.Text.Json;
using Aether.Modding;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Aether.Bootstrap;

internal sealed class Selectors
{
    private sealed record MappingFile(int SchemaVersion, Dictionary<string, string>? Selectors);
    private readonly Dictionary<string, string> mappings;

    internal Selectors(string? path)
    {
        if (path == null)
        {
            mappings = new(StringComparer.Ordinal);
            return;
        }
        var file = JsonSerializer.Deserialize<MappingFile>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (file?.SchemaVersion != 1 || file.Selectors == null || file.Selectors.Values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Expected a schemaVersion 1 mapping file with a selectors object.");
        mappings = new(file.Selectors, StringComparer.Ordinal);
    }

    internal string Map(string name) => mappings.GetValueOrDefault(name, name);

    internal string Describe(string name) => name == Map(name) ? $"'{name}'" : $"'{name}' (mapped to '{Map(name)}')";

    internal static string Signature(MethodReference method) =>
        $"{method.Name}({string.Join(",", method.Parameters.Select(parameter => parameter.ParameterType.FullName))}):{method.ReturnType.FullName}";

    internal static string Signature(FieldReference field) => $"{field.Name}:{field.FieldType.FullName}";

    internal static bool MemberMatches(MethodReference method, string selector)
    {
        if (selector == method.Name)
            return true;
        string signature = Signature(method);
        return selector == signature || selector == signature[..(signature.LastIndexOf("):", StringComparison.Ordinal) + 1)];
    }

    internal static bool MemberMatches(FieldReference field, string selector) => selector == field.Name || selector == Signature(field);

    private static bool QualifiedMatches(MemberReference member, string selector)
    {
        int delimiter = selector.IndexOf("::", StringComparison.Ordinal);
        if (delimiter < 0 || member.DeclaringType.FullName != selector[..delimiter])
            return false;
        string signature = selector[(delimiter + 2)..];
        return member switch
        {
            MethodReference method => MemberMatches(method, signature),
            FieldReference field => MemberMatches(field, signature),
            _ => false
        };
    }

    internal MethodDefinition? Method(TypeDefinition type, string name)
    {
        var candidates = type.Methods.Where(method => MemberMatches(method, Map(name))).ToArray();
        return candidates.Length switch
        {
            0 => null,
            1 => candidates[0],
            _ => throw new InvalidDataException($"Ambiguous method {Describe(name)} in {type.FullName}; specify its signature.")
        };
    }

    internal IEnumerable<int> Sites(Instruction[] instructions, At at, string? selector, int ordinal, int start, int end)
    {
        if (ordinal < -1)
            throw new InvalidDataException("Ordinal must be -1 or nonnegative.");
        if (at is At.INVOKE or At.FIELD or At.NEW or At.CONSTANT && string.IsNullOrEmpty(selector))
            throw new InvalidDataException($"{at} needs a target selector.");
        if (!Enum.IsDefined(at))
            throw new InvalidDataException($"Unknown injection point {at}.");

        string? target = selector == null ? null : Map(selector);
        IEnumerable<int> matches = Enumerable.Range(start, end - start + 1).Where(i => at switch
        {
            At.HEAD => i == start,
            At.RETURN or At.TAIL => instructions[i].OpCode.Code == Code.Ret,
            At.INVOKE => instructions[i].OpCode.Code is Code.Call or Code.Callvirt
                && instructions[i].Operand is MethodReference method && QualifiedMatches(method, target!),
            At.NEW => instructions[i].OpCode.Code == Code.Newobj
                && instructions[i].Operand is MethodReference constructor && QualifiedMatches(constructor, target!),
            At.FIELD => instructions[i].OpCode.Code is Code.Ldfld or Code.Ldsfld or Code.Stfld or Code.Stsfld
                && instructions[i].Operand is FieldReference field && QualifiedMatches(field, target!),
            At.CONSTANT => Literal(instructions[i]) is { } literal && literal.Selector == target,
            _ => false
        });

        if (at == At.TAIL)
            matches = matches.TakeLast(1);
        return ordinal == -1 ? matches : matches.Skip(ordinal).Take(1);
    }

    internal static (string Selector, MetadataType Type)? Literal(Instruction instruction) => instruction.OpCode.Code switch
    {
        Code.Ldc_I4_M1 => ("int:-1", MetadataType.Int32),
        >= Code.Ldc_I4_0 and <= Code.Ldc_I4_8 => ($"int:{instruction.OpCode.Code - Code.Ldc_I4_0}", MetadataType.Int32),
        Code.Ldc_I4 or Code.Ldc_I4_S => ($"int:{Number(instruction.Operand)}", MetadataType.Int32),
        Code.Ldc_I8 => ($"long:{Number(instruction.Operand)}", MetadataType.Int64),
        Code.Ldc_R4 => ($"float:{Number(instruction.Operand)}", MetadataType.Single),
        Code.Ldc_R8 => ($"double:{Number(instruction.Operand)}", MetadataType.Double),
        Code.Ldstr => ($"string:{instruction.Operand}", MetadataType.String),
        Code.Ldnull => ("null", MetadataType.Object),
        _ => null
    };

    private static string Number(object value) => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);

    internal static string? Text(CustomAttribute attribute, int index) =>
        index < attribute.ConstructorArguments.Count ? attribute.ConstructorArguments[index].Value as string : null;

    internal static T Option<T>(CustomAttribute attribute, string name, T fallback)
    {
        foreach (CustomAttributeNamedArgument property in attribute.Properties)
        {
            if (property.Name != name)
                continue;
            return typeof(T).IsEnum
                ? (T)Enum.ToObject(typeof(T), property.Argument.Value)
                : (T)property.Argument.Value;
        }
        return fallback;
    }

    internal static CustomAttribute? Attribute(ICustomAttributeProvider member, string name) =>
        member.CustomAttributes.SingleOrDefault(attribute => attribute.AttributeType.FullName == $"Aether.Modding.{name}Attribute");
}
