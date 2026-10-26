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

    /// <summary>Damage waiting to be applied this tick. Server only; any source (guns, crashes, nuke) appends to it.</summary>
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    [InternalBufferCapacity(4)]
    public struct DamageEvent : IBufferElementData
    {
        public float Amount;
        /// <summary>The chopper that caused the damage, or <see cref="Entity.Null"/> (environment).</summary>
        public Entity Source;
    }

    /// <summary>Server to victim: you took damage. Drives the damage direction indicator and camera shake.</summary>
    public struct DamageTakenRpc : IRpcCommand
    {
        public float Amount;
        /// <summary>World position of the attacker when it hit. Only valid if <see cref="HasSource"/>.</summary>
        public float3 SourcePosition;
        /// <summary>False for crash and bounds damage.</summary>
        public bool HasSource;
    }

    /// <summary>Server to shooter: your shot hit. Drives the hit marker, so the client never shows a fake hit.</summary>
    public struct HitConfirmRpc : IRpcCommand
    {
        public float Damage;
        public bool Killed;
    }
}
