# Recipes

Register a recipe in the mod initializer with a unique `mod-id:name`.
Use positive quantities and vanilla item IDs, and supply an optional vanilla tile ID for the crafting station.

```csharp
RecipeRegistry.Register("example:torch", new RecipeDefinition(
    Result: new ItemAmount(8, 1),
    Ingredients: [new ItemAmount(9, 1)]));
```

List each ingredient once and keep item quantities within the item's maximum stack size.
Recipes use the game's ingredient and total recipe limits.
The API appends registered recipes after vanilla recipe setup in registration order.
Registered recipes appear in the recipe list and the Guide in single-player, and they do not participate in Shimmer decrafting.
These recipes do not add item IDs to saved characters or worlds.
