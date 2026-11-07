using Aether.Modding;

namespace Aether.Api;

public sealed class EntitySprite
{
    private readonly ClientEntitySprite sprite;

    private EntitySprite(ClientEntitySprite sprite) => this.sprite = sprite;

    public static EntitySprite Vanilla(int textureId, int ticksPerFrame = 6)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureId);
        ValidateTicks(ticksPerFrame);
        return new(new ClientEntitySprite(textureId, null, 0, ticksPerFrame));
    }

    public static EntitySprite Resource(string path, int frames = 1, int ticksPerFrame = 6)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An entity sprite resource must be a PNG file.", nameof(path));
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frames, 64);
        ValidateTicks(ticksPerFrame);
        return new(new ClientEntitySprite(0, path, frames, ticksPerFrame));
    }

    internal ClientEntitySprite Resolve(string id, string owner, int vanillaCount)
    {
        if (sprite.ResourcePath is { } path)
        {
            try
            {
                using var resource = ModResources.OpenRead(owner, path);
            }
            catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
            {
                throw new InvalidDataException($"Cannot open sprite '{path}' for entity '{id}'.", error);
            }
        }
        else if (sprite.VanillaType >= vanillaCount)
        {
            throw new InvalidDataException($"Vanilla sprite {sprite.VanillaType} is unavailable for entity '{id}'.");
        }
        return sprite;
    }

    private static void ValidateTicks(int ticksPerFrame)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ticksPerFrame, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ticksPerFrame, 600);
    }
}

public sealed class EntityBehaviorState
{
    internal EntityBehaviorState() { }
    public object? Value { get; set; }
}
