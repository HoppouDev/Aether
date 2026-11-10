using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Net;

namespace Aether.Modding;

internal sealed class EntityWire : NetModule
{
    private const int PayloadSize = 56;
    private const int SyncInterval = 6;
    private static long sequence;

    private struct Replica
    {
        public long Identity;
        public long Sequence;
        public bool Retired;
        public Entity Entity;
    }

    private static readonly Replica[] npcReplicas = new Replica[Main.maxNPCs];
    private static readonly Replica[] projectileReplicas = new Replica[Main.maxProjectiles];

    internal static bool IsAuthority => Main.netMode == 2 && Main.dedServ && !Netplay.Disconnect && ClientContent.HasEntities &&
        ClientContent.ConnectionVersion != null;

    internal static void ResetClient()
    {
        if (!ClientContent.HasEntities)
            return;
        Array.Clear(npcReplicas);
        Array.Clear(projectileReplicas);
    }

    internal static void VanillaNpcReceived(int slot)
    {
        if (ClientContent.HasEntities && npcReplicas[slot].Entity is NPC npc)
        {
            npc.active = false;
            npcReplicas[slot].Entity = null;
            npcReplicas[slot].Retired = true;
        }
    }

    internal static long NpcIdentity(int slot) => npcReplicas[slot].Identity;

    internal static bool CanStrike(NPC npc, int sender, int damage, float knockback, int direction, int critical)
    {
        Player player = Main.player[sender];
        return IsAuthority && Netplay.Clients[sender].State == 10 && player.active && !player.dead &&
            damage > 0 && npc.immune[sender] == 0 && knockback is >= 0 and <= 64 &&
            direction is >= -1 and <= 1 && critical <= 1 &&
            Vector2.DistanceSquared(player.Center, npc.Center) <= 2400 * 2400;
    }

    internal static void Tick()
    {
        if (IsAuthority && Main.GameUpdateCount % SyncInterval == 0)
            SendAll(-1);
    }

    internal static void SendInitial(int client)
    {
        if (IsAuthority)
            SendAll(client);
    }

    private static void SendAll(int client)
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && npc.AetherIsCustom)
                SyncNpc(npc, client);
        }
        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (projectile.active && projectile.AetherIsCustom)
                SyncProjectile(projectile, client);
        }
    }

    internal static void SyncNpc(NPC npc, int client = -1)
    {
        if (IsAuthority)
            Send(npc, npc.active, family: 0, npc.netID, npc.generation, npc.life, npc.lifeMax, npc.damage,
                npc.rotation, npc.spriteDirection, client);
    }

    internal static void SyncProjectile(Projectile projectile, int client = -1)
    {
        if (IsAuthority)
            Send(projectile, projectile.active, family: 1, projectile.type, 0, projectile.timeLeft, 0, projectile.damage,
                projectile.rotation, projectile.spriteDirection, client);
    }

    private static void Send(Entity entity, bool active, byte family, int type, byte generation, int value, int maximum, int damage,
        float rotation, int direction, int client)
    {
        NetPacket packet = CreatePacket<EntityWire>(PayloadSize);
        BinaryWriter writer = packet.Writer;
        writer.Write(family);
        writer.Write(active);
        writer.Write((ushort)entity.whoAmI);
        writer.Write((short)type);
        writer.Write(entity.AetherSpawnIdentity);
        writer.Write(System.Threading.Interlocked.Increment(ref sequence));
        writer.Write(generation);
        writer.Write(value);
        writer.Write(maximum);
        writer.Write(damage);
        writer.WriteVector2(entity.position);
        writer.WriteVector2(entity.velocity);
        writer.Write(rotation);
        writer.Write((sbyte)Math.Sign(direction));
        if (client >= 0)
            NetManager.Instance.SendToClient(packet, client);
        else
            NetManager.Instance.Broadcast(packet, static index => Netplay.Clients[index].State == 10);
    }

    public override bool Deserialize(BinaryReader reader, int userId) => false;

    internal override bool Deserialize(BinaryReader reader, int userId, int payloadLength)
    {
        if (Main.netMode != 1 || !ClientContent.HasEntities || ClientContent.ConnectionVersion == null ||
            Netplay.Connection.State < 3 || payloadLength != PayloadSize)
            return false;

        byte family = reader.ReadByte();
        byte active = reader.ReadByte();
        int slot = reader.ReadUInt16();
        int type = reader.ReadInt16();
        long identity = reader.ReadInt64();
        long update = reader.ReadInt64();
        byte generation = reader.ReadByte();
        int value = reader.ReadInt32();
        int maximum = reader.ReadInt32();
        int damage = reader.ReadInt32();
        Vector2 position = reader.ReadVector2();
        Vector2 velocity = reader.ReadVector2();
        float rotation = reader.ReadSingle();
        int direction = reader.ReadSByte();
        if (family > 1 || active > 1 || identity <= 0 || update <= 0 || direction is < -1 or > 1 ||
            (family == 0 ? slot >= npcReplicas.Length || type < NPCID.VanillaCount || type >= NPCID.Count || generation == 0
                : slot >= projectileReplicas.Length || type < ProjectileID.VanillaCount || type >= ProjectileID.Count || generation != 0))
            return false;
        if (active != 0 && (!IsBounded(position, Main.maxTilesX * 16 + 4096, Main.maxTilesY * 16 + 4096) ||
            !IsBounded(velocity, ClientContent.MaxReplicatedEntityVelocity, ClientContent.MaxReplicatedEntityVelocity) || !float.IsFinite(rotation) ||
            damage is < 0 or > 1_000_000 || (family == 0 ? maximum is < 1 or > 20_000_000 || value < 1 || value > maximum
                : maximum != 0 || value is < 1 or > 36_000)))
            return false;

        ref Replica replica = ref (family == 0 ? ref npcReplicas[slot] : ref projectileReplicas[slot]);
        if (identity < replica.Identity || update <= replica.Sequence ||
            (identity == replica.Identity && active != 0 && replica.Retired))
            return false;
        if (identity == replica.Identity &&
            (replica.Entity is NPC previousNpc && (previousNpc.netID != type || previousNpc.generation != generation) ||
             replica.Entity is Projectile previousProjectile && previousProjectile.type != type))
            return false;

        if (identity != replica.Identity || active == 0)
        {
            ClearReplica(ref replica);
            replica.Identity = identity;
        }
        replica.Sequence = update;
        replica.Retired = active == 0;
        if (active == 0)
            return true;

        if (family == 0)
        {
            if (replica.Entity is not NPC npc)
            {
                npc = NPC.NewNPCInstanceInSlot(slot, generation);
                npc.SetDefaults(type);
                replica.Entity = npc;
            }
            npc.life = value;
            npc.lifeMax = maximum;
            npc.damage = damage;
            npc.rotation = rotation;
            npc.direction = npc.spriteDirection = direction;
        }
        else
        {
            if (replica.Entity is not Projectile projectile || !projectile.active)
            {
                projectile = Projectile.NewProjectileSetup(allowEviction: false);
                if (projectile == null)
                    return false;
                projectile.SetDefaults(type);
                projectile.owner = Main.maxPlayers;
                replica.Entity = projectile;
            }
            projectile.timeLeft = value;
            projectile.damage = damage;
            projectile.rotation = rotation;
            projectile.direction = projectile.spriteDirection = direction;
        }
        replica.Entity.position = position;
        replica.Entity.velocity = velocity;
        return true;
    }

    private static void ClearReplica(ref Replica replica)
    {
        if (replica.Entity is NPC npc)
            npc.active = false;
        else if (replica.Entity is Projectile projectile)
            projectile.active = false;
        replica.Entity = null;
    }

    private static bool IsBounded(Vector2 value, float xLimit, float yLimit) =>
        Math.Abs(value.X) <= xLimit && Math.Abs(value.Y) <= yLimit;

    internal static void DamageNpcContacts(NPC npc)
    {
        if (npc.friendly || npc.damage <= 0)
            return;
        Rectangle hitbox = npc.Hitbox;
        for (int i = 0; i < Main.maxPlayers && npc.active; i++)
        {
            Player player = Main.player[i];
            if (!hitbox.Intersects(player.Hitbox) || player.npcTypeNoAggro[npc.type] || !CanHurt(player))
                continue;
            int damage = HurtPlayer(player, npc, Main.DamageVar(npc.damage, -player.luck), PlayerDeathReason.ByNPC(npc.whoAmI));
            if (damage > 0)
                ClientContent.NpcHitPlayer?.Invoke(npc, player, damage);
        }
    }

    internal static void DamageProjectileContacts(Projectile projectile, Rectangle hitbox)
    {
        for (int i = 0; i < Main.maxPlayers && projectile.active; i++)
        {
            Player player = Main.player[i];
            if (!hitbox.Intersects(player.Hitbox) || !CanHurt(player))
                continue;
            int raw = (int)(Main.DamageVar(projectile.damage, 0) * projectile.hostileDamageScaling.Sample(Main.Difficulty)) * 2;
            int damage = HurtPlayer(player, projectile, raw, PlayerDeathReason.ByAetherProjectile(player, projectile.type));
            if (damage == 0)
                continue;
            ClientContent.ProjectileHitPlayer?.Invoke(projectile, player, damage);
            if (projectile.penetrate > 0 && --projectile.penetrate == 0)
                break;
        }
    }

    private static bool CanHurt(Player player) => player.active && !player.dead && !player.immune &&
        Netplay.Clients[player.whoAmI].State == 10;

    private static int HurtPlayer(Player player, Entity source, int damage, PlayerDeathReason reason)
    {
        int raw = Math.Clamp(damage, 1, short.MaxValue);
        int direction = player.Center.X < source.Center.X ? -1 : 1;
        int dealt = (int)player.Hurt(reason, raw, direction, quiet: true, dodgeable: false);
        if (dealt > 0)
            NetMessage.SendPlayerHurt(player.whoAmI, reason, raw, direction, false, false, ImmunityCooldownID.General);
        return dealt;
    }
}
