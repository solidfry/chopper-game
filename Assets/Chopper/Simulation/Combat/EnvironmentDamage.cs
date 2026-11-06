using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Crash damage from the impact speed at the contact: how much velocity the physics step took away from the chopper
    /// (walls, roofs, the ground, another chopper). Works from velocity, so colliders do not need "raise collision
    /// events". Landing (a contact from below) has its own safe speed: skids take a firm touchdown.
    /// Damage is a share of the chopper's max health, so every chassis feels the same speeds.
    /// </summary>
    public struct CrashDamageConfig : IComponentData
    {
        /// <summary>Impact speed (m/s) against a wall, a ceiling or another chopper that does no damage.</summary>
        public float SafeImpactSpeed;
        /// <summary>Vertical touchdown speed (m/s) on the ground or a roof that does no damage.</summary>
        public float SafeLandingSpeed;
        /// <summary>Impact speed (m/s) that takes all the health in one hit.</summary>
        public float LethalImpactSpeed;
        /// <summary>Curve between safe and lethal: 1 = straight line, above 1 = bumps are mild, big crashes hard.</summary>
        public float Curve;

        public static CrashDamageConfig Default => new()
        {
            SafeImpactSpeed = 5f,
            SafeLandingSpeed = 7f,
            LethalImpactSpeed = 45f,
            Curve = 1.5f,
        };
    }

    /// <summary>Server only: velocity before this tick's physics step (after the flight forces).</summary>
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    public struct CrashSensor : IComponentData
    {
        public float3 VelocityBefore;
        /// <summary>False until the first snapshot (a new chopper takes no crash damage on its first tick).</summary>
        public bool HasVelocity;
        /// <summary>Velocity the contacts took away since this contact started (see <see cref="CrashDamage.Track"/>).</summary>
        public float3 ContactChange;
        /// <summary>Seconds since this contact started.</summary>
        public float ContactAge;
        /// <summary>Damage this contact already did.</summary>
        public float ContactDealt;
        public bool InContact;
    }

    /// <summary>Crash damage maths, shared by the server system and the tests.</summary>
    public static class CrashDamage
    {
        /// <summary>A contact whose normal points up this much (cos 45°) is ground or a roof: a landing.</summary>
        public const float GroundNormalY = 0.7f;
        /// <summary>Physics spreads one hit over a few ticks (21.8 m/s came as 18.4 + 3.4): they count as one impact.</summary>
        public const float ContactWindow = 0.1f;
        /// <summary>A velocity change below this (m/s per tick) is gravity and drag, not a contact.</summary>
        public const float ContactMinChange = 1f;

        /// <summary>
        /// Adds this tick's velocity change to the contact. Returns false when there is no contact this tick. Else
        /// <paramref name="total"/> = the whole impact speed so far, <paramref name="added"/> = the part new this tick
        /// (the camera shake adds up, so it takes only the new part) and <paramref name="normal"/> = the contact normal.
        /// </summary>
        public static bool Track(ref CrashSensor sensor, float3 change, float deltaTime, out float total, out float added, out float3 normal)
        {
            total = added = 0f;
            normal = new float3(0f, 1f, 0f);
            if (sensor.InContact)
            {
                sensor.ContactAge += deltaTime;
                if (sensor.ContactAge > ContactWindow) sensor.InContact = false;
            }
            if (math.length(change) < ContactMinChange) return false;
            if (!sensor.InContact)
            {
                sensor.InContact = true;
                sensor.ContactAge = 0f;
                sensor.ContactChange = float3.zero;
                sensor.ContactDealt = 0f;
            }
            var before = math.length(sensor.ContactChange);
            sensor.ContactChange += change;
            total = math.length(sensor.ContactChange);
            added = math.max(0f, total - before);
            if (total > 1e-4f) normal = sensor.ContactChange / total;
            return true;
        }


        /// <summary>
        /// Speed change the physics step made (after minus before). The contact pushed the chopper along its normal,
        /// so the change is the impact speed and its direction is the surface normal. Gravity and drag in the step
        /// are far below the safe speeds.
        /// </summary>
        public static float ImpactSpeed(float3 velocityBefore, float3 velocityAfter, out float3 normal)
        {
            var change = velocityAfter - velocityBefore;
            var speed = math.length(change);
            normal = speed > 1e-4f ? change / speed : new float3(0f, 1f, 0f);
            return speed;
        }

        public static bool IsLanding(float3 normal) => normal.y >= GroundNormalY;

        /// <summary>Damage for one impact: 0 at the safe speed, <paramref name="maxHealth"/> at the lethal speed.</summary>
        public static float Damage(in CrashDamageConfig config, float impactSpeed, bool landing, float maxHealth)
        {
            var safe = landing ? config.SafeLandingSpeed : config.SafeImpactSpeed;
            var range = math.max(0.1f, config.LethalImpactSpeed - safe);
            var share = math.saturate((impactSpeed - safe) / range);
            if (share <= 0f) return 0f;
            return maxHealth * math.pow(share, math.max(0.1f, config.Curve));
        }
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
