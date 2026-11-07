using Terraria;

namespace Aether.Api;

public enum WorldDifficulty
{
    Classic,
    Expert,
    Master,
    Journey
}

public readonly record struct WorldSnapshot(string Name, int Id, WorldDifficulty Difficulty, bool IsMultiplayer,
    bool Hardmode, bool IsDay, double Time, bool IsRaining, float WindSpeed, TilePosition Spawn, int Width, int Height);

public static class Worlds
{
    public static bool TryGetCurrent(out WorldSnapshot world)
    {
        Registration.RequireGameThread();
        world = default;
        if (!GameBridge.WorldReady || Main.ActiveWorldFileData is not { } current)
            return false;

        world = new WorldSnapshot(current.GetWorldName(), current.WorldId, (WorldDifficulty)Main.GameMode, Main.netMode != 0,
            Main.hardMode, Main.dayTime, Main.time, Main.raining, Main.windSpeedCurrent,
            new TilePosition(Main.spawnTileX, Main.spawnTileY), Main.maxTilesX, Main.maxTilesY);
        return true;
    }
}
