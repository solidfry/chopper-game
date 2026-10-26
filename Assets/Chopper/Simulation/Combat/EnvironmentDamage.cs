using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Crash damage from sudden speed loss (hitting the ground, a wall, another chopper). Works from velocity, so
    /// colliders do not need "raise collision events".
    /// </summary>
    public struct CrashDamageConfig : IComponentData
    {
        /// <summary>Speed loss in one tick (m/s) that does no damage. Soft landings and drag stay under it.</summary>
        public float SafeSpeedLoss;
        /// <summary>Damage per m/s of speed loss above <see cref="SafeSpeedLoss"/>.</summary>
        public float DamagePerSpeedLoss;
    }

    /// <summary>Server only: speed after the last physics step.</summary>
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    public struct CrashSensor : IComponentData
    {
        public float PreviousSpeed;
    }

    /// <summary>
    /// The playable area (a box). Outside it a chopper takes damage every second and the HUD warns the player.
    /// Singleton, baked from <see cref="ArenaBoundsAuthoring"/> in the gameplay subscene (client and server).
    /// </summary>
    public struct ArenaBounds : IComponentData
    {
        public float3 Center;
        public float3 HalfExtents;
        public float DamagePerSecond;

        public bool Contains(float3 position) => math.all(math.abs(position - Center) <= HalfExtents);
    }

    /// <summary>
    /// Server only: who last hit this chopper, so a crash or bounds death soon after still counts as their kill.
    /// </summary>
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    public struct LastAttacker : IComponentData
    {
        /// <summary>Network id of the attacker, 0 = none.</summary>
        public int NetworkId;
        public double Time;
    }
}
