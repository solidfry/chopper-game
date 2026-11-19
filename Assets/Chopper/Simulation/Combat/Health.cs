using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Hit points. Only the server writes it (see <see cref="ApplyDamageSystem"/>); clients get it as a ghost field
    /// and never change it.
    /// </summary>
    public struct Health : IComponentData
    {
        [GhostField(Quantization = 10)] public float Current;
        /// <summary>Baked on the prefab, same on client and server, so it is not sent.</summary>
        public float Max;

        public bool IsDead => Current <= 0f;

        /// <summary>Removes hit points. Returns true only for the hit that kills.</summary>
        public bool TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f) return false;
            Current = math.max(0f, Current - amount);
            return IsDead;
        }
    }

    /// <summary>What did the damage. Drives the hit effect on clients (sparks, flash, dust).</summary>
    public enum DamageKind : byte
    {
        /// <summary>No hit point (old callers): no hit effect.</summary>
        None = 0,
        Bullet = 1,
        Explosion = 2,
        /// <summary>Hit a wall, a roof or another chopper.</summary>
        Impact = 3,
        /// <summary>Came down too fast on the ground or a roof.</summary>
        Landing = 4,
        /// <summary>Out of the arena: no hit point.</summary>
        Bounds = 5,
        /// <summary>Damage over time from corroding gun hits (pilot passive): no hit point, no shake.</summary>
        Corrosion = 6,
        /// <summary>Damage over time from burning blast hits (pilot passive): no hit point, no shake.</summary>
        Burn = 7,
        /// <summary>Resting on its side, roof, nose or tail: damage over time until upright (no hit point, no shake).</summary>
        Toppled = 8,
    }

    /// <summary>
    /// Server only. What damage needs from its attacker after the attacker's chopper may be gone: the player
    /// (<see cref="NetworkId"/>, from <see cref="GhostOwner"/>; server bots have one too), the connection for the hit marker
    /// (<see cref="Entity.Null"/> for bots) and the pilot traits at fire time. Build it with <see cref="AttackerSnapshot.Of"/>.
    /// </summary>
    public struct AttackerSnapshot
    {
        public int NetworkId;
        public Entity Connection;
        public PilotTraits Traits;
        /// <summary>The shooter's ability boost to burns from bullet hits (Venom Belt) at fire time. 1 = none.</summary>
        public float BulletDotScale;
        /// <summary>The shooter's ability boost to burns from explosion hits (Napalm Salvo) at fire time. 1 = none.</summary>
        public float ExplosionDotScale;
        public bool Valid;

        public static AttackerSnapshot Of(Entity shooter, in ComponentLookup<GhostOwner> owners,
            in ComponentLookup<Session.ConnectionOwner> connections, in ComponentLookup<PilotTraits> traits,
            in ComponentLookup<AbilityConfig> abilityConfigs, in ComponentLookup<AbilityState> abilities)
        {
            var snapshot = new AttackerSnapshot
            {
                NetworkId = owners.TryGetComponent(shooter, out var owner) ? owner.NetworkId : 0,
                Connection = connections.TryGetComponent(shooter, out var connection) ? connection.Value : Entity.Null,
                Traits = traits.TryGetComponent(shooter, out var pilot) ? pilot : PilotTraits.None,
                BulletDotScale = 1f,
                ExplosionDotScale = 1f,
                Valid = shooter != Entity.Null,
            };
            if (abilityConfigs.TryGetComponent(shooter, out var config) && abilities.TryGetComponent(shooter, out var ability))
            {
                snapshot.BulletDotScale = Abilities.DotScale(config, ability, DamageKind.Bullet);
                snapshot.ExplosionDotScale = Abilities.DotScale(config, ability, DamageKind.Explosion);
            }
            return snapshot;
        }

        /// <summary>The saved ability boost for a burn made by a hit of this kind. 1 = none.</summary>
        public readonly float DotScale(DamageKind hitKind) =>
            hitKind == DamageKind.Bullet ? BulletDotScale : hitKind == DamageKind.Explosion ? ExplosionDotScale : 1f;
    }

    /// <summary>Damage waiting to be applied this tick. Server only; any source (guns, crashes, nuke) appends to it.</summary>
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    [InternalBufferCapacity(4)]
    public struct DamageEvent : IBufferElementData
    {
        public float Amount;
        /// <summary>The chopper that caused the damage, or <see cref="Entity.Null"/> (environment).</summary>
        public Entity Source;
        /// <summary>World point where it hit the victim. Used only when <see cref="HasPoint"/>.</summary>
        public float3 Point;
        /// <summary>World direction out of the victim's surface at <see cref="Point"/> (sparks fly this way).</summary>
        public float3 Normal;
        public DamageKind Kind;
        /// <summary>Crashes and landings: the impact speed (m/s), for the victim's camera shake. 0 for other hits.</summary>
        public float ImpactSpeed;
        /// <summary>Gun hits: metres the round flew from the muzzle, for the match log. 0 = not set (the log uses the
        /// distance between the choppers).</summary>
        public float FlightDistance;
        /// <summary>The weapon's short name, for the match log; empty for crashes, bounds and damage over time.</summary>
        public Unity.Collections.FixedString32Bytes Weapon;
        /// <summary>
        /// Who fired it, saved when the shot left the muzzle. Used only when <see cref="Source"/> no longer exists (the
        /// shooter's chopper died while a round or a burn was still on its way), so kill credit, the hit marker and the
        /// pilot's traits still count. Default (not <see cref="AttackerSnapshot.Valid"/>) for damage made on the spot.
        /// </summary>
        public AttackerSnapshot Attacker;

        public bool HasPoint => Kind is DamageKind.Bullet or DamageKind.Explosion or DamageKind.Impact or DamageKind.Landing;
    }

    /// <summary>
    /// Server to every client: a ghost with health was hit at this point. Drives the sparks at the hit and where the
    /// damage smoke comes out later. The point is in the victim's own space, so it stays on the hull as it moves.
    /// At most one per victim per tick (the strongest hit).
    /// </summary>
    public struct ChopperHitRpc : IRpcCommand
    {
        /// <summary><c>GhostInstance.ghostId</c> of the victim.</summary>
        public int GhostId;
        public float3 LocalPoint;
        public float3 LocalNormal;
        public float Amount;
        /// <summary>A <see cref="DamageKind"/> (sent as a byte, like the other RPC enums).</summary>
        public byte Kind;
    }

    /// <summary>Server to victim: you took damage. Drives the damage direction indicator and camera shake.</summary>
    public struct DamageTakenRpc : IRpcCommand
    {
        public float Amount;
        /// <summary>World position of the attacker when it hit. Only valid if <see cref="HasSource"/>.</summary>
        public float3 SourcePosition;
        /// <summary>False for crash and bounds damage.</summary>
        public bool HasSource;
        /// <summary>A <see cref="DamageKind"/> as a byte: the camera shake matches the kind of hit.</summary>
        public byte Kind;
        /// <summary>Crashes and landings: impact speed (m/s). 0 for other hits.</summary>
        public float ImpactSpeed;
        /// <summary>The attacker's pilot jams HUDs (HEX): the victim's HUD glitches.</summary>
        public bool Jam;
    }

    /// <summary>Server to shooter: your shot hit. Drives the hit marker, so the client never shows a fake hit.</summary>
    public struct HitConfirmRpc : IRpcCommand
    {
        public float Damage;
        public bool Killed;
    }
}
