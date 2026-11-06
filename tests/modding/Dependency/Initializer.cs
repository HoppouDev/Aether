using Aether.Modding;

public sealed class DependencyInit : IModInitializer
{
    public void Initialize() => System.Console.WriteLine("DEPENDENCY");
}

[Mixin("Target")]
public static class DependencyMixin
{
    [Inject("Entry", At.HEAD)]
    private static void CheckSharedLibrary(CallbackInfoReturnable<int> callback)
    {
        if (Aether.Tests.Numbers.Increase(1) != 3)
            throw new System.InvalidOperationException("Wrong dependency loaded.");
    }
}

public sealed class MetadataInit : IModInitializer
{
    public void Initialize()
    {
        var mod = System.Linq.Enumerable.Single(ModCatalog.LoadedMods);
        if (mod.Id != "metadata")
            throw new System.InvalidOperationException("Wrong mod in catalog.");

        if (mod.Name == mod.Id)
        {
            if (mod.Description.Length != 0 || mod.Authors.Count != 0 ||
                mod.Contributors.Count != 0 || mod.Source != null || mod.Homepage != null ||
                mod.Licenses.Count != 0 || mod.IconPath != null)
                throw new System.InvalidOperationException("Wrong metadata defaults.");

            try
            {
                using var icon = ModResources.OpenIcon(mod.Id);
                throw new System.InvalidOperationException("A nested icon was accepted as the mod icon.");
            }
            catch (System.IO.FileNotFoundException) { }
        }
        else
        {
            if (mod.Name != "Metadata Example" || mod.Description != "A fixture mod" ||
                mod.Authors.Count != 2 || mod.Authors[0] != "Alex" ||
                mod.Authors[1] != "Sam" ||
                mod.Contributors[0] != "Taylor" ||
                mod.Source != "https://example.org/inventory-tools/source" ||
                mod.Homepage != "https://example.org/inventory-tools" ||
                mod.Licenses[0] != "MIT" || mod.IconPath != "icon.png")
                throw new System.InvalidOperationException("Wrong display metadata.");

            using var icon = ModResources.OpenIcon(mod.Id);
            if (icon.ReadByte() != 137)
                throw new System.InvalidOperationException("Wrong icon resource.");
        }

        System.Console.WriteLine("METADATA OK");
    }
}
