using Terraria;
using Terraria.ID;

namespace Aether.Api;

public readonly record struct ItemAmount(int ItemId, int Quantity);
public sealed record RecipeDefinition(ItemAmount Result, IReadOnlyList<ItemAmount> Ingredients, int? Station = null);

public static class RecipeRegistry
{
    private static readonly Dictionary<string, RecipeDefinition> entries = [];
    private static readonly HashSet<Recipe> installed = [];

    public static void Register(string id, RecipeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Ingredients);
        var ingredients = definition.Ingredients.ToArray();
        if (ingredients.Length == 0 || ingredients.Select(item => item.ItemId).Distinct().Count() != ingredients.Length)
            throw new ArgumentException("Specify at least one ingredient and list each item once.", nameof(definition));
        ValidateAmount(definition.Result);
        foreach (ItemAmount ingredient in ingredients)
            ValidateAmount(ingredient);
        if (definition.Station is int station && (station < 0 || station >= TileID.Count))
            throw new ArgumentException("Crafting station must be a vanilla tile ID.", nameof(definition));

        Registration.Add(entries, id, definition with { Ingredients = ingredients }, Registration.Owner());
    }

    private static void ValidateAmount(ItemAmount amount)
    {
        if (amount.ItemId <= 0 || amount.ItemId >= ItemID.VanillaCount || amount.Quantity <= 0)
            throw new ArgumentException("Recipe items must use vanilla IDs and positive quantities.");
    }

    internal static void Install()
    {
        if (entries.Count > Main.recipe.Length - Recipe.numRecipes)
            throw new InvalidOperationException("The vanilla recipe table has insufficient capacity for Aether API recipes.");
        installed.Clear();
        foreach (var (id, definition) in entries)
        {
            if (definition.Ingredients.Count > Recipe.maxRequirements)
                throw new InvalidOperationException($"Recipe '{id}' exceeds the game's ingredient limit.");
            var recipe = new Recipe();
            SetItem(recipe.createItem, definition.Result, id);
            for (int index = 0; index < definition.Ingredients.Count; index++)
            {
                ItemAmount ingredient = definition.Ingredients[index];
                SetItem(recipe.requiredItem[index], ingredient, id);
                recipe.requiredItemQuickLookup[index] = new Recipe.RequiredItemEntry(ingredient.ItemId, ingredient.Quantity);
            }
            recipe.notDecraftable = true;
            recipe.requiredTile = definition.Station ?? -1;
            recipe.alchemy = recipe.requiredTile == 13;
            if (recipe.requiredTile >= 0)
                Recipe.TileUsedInRecipes[recipe.requiredTile] = true;
            Main.recipe[Recipe.numRecipes++] = recipe;
            installed.Add(recipe);
        }
    }

    private static void SetItem(Item item, ItemAmount amount, string id)
    {
        item.SetDefaults(amount.ItemId);
        if (item.IsAir || amount.Quantity > item.maxStack)
            throw new InvalidOperationException($"Recipe '{id}' uses an unavailable item or an invalid stack for item {amount.ItemId}.");
        item.stack = amount.Quantity;
    }

    internal static bool IsBlocked(Recipe recipe) => Main.netMode != 0 && installed.Contains(recipe);
}
