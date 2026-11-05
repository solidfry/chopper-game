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
    }

    /// <summary>Server to shooter: your shot hit. Drives the hit marker, so the client never shows a fake hit.</summary>
    public struct HitConfirmRpc : IRpcCommand
    {
        public float Damage;
        public bool Killed;
    }
}
