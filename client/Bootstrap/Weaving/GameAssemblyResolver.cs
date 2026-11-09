using Mono.Cecil;

namespace Aether.Bootstrap;

internal sealed class GameAssemblyResolver : DefaultAssemblyResolver
{
    internal ModuleDefinition? Game { get; set; }

    public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        try
        {
            return base.Resolve(name, parameters);
        }
        catch (AssemblyResolutionException) when (Game != null)
        {
            string suffix = name.Name + ".dll";
            var resource = Game.Resources.OfType<EmbeddedResource>().FirstOrDefault(candidate =>
                candidate.Name.Equals(suffix, StringComparison.Ordinal) || candidate.Name.EndsWith("." + suffix, StringComparison.Ordinal));
            if (resource == null)
                throw;

            return AssemblyDefinition.ReadAssembly(new MemoryStream(resource.GetResourceData(), writable: false), parameters);
        }
    }
}
