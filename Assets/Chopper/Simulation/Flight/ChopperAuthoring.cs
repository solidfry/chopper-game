using Chopper.Simulation.Combat;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// Put on the chopper ghost prefab together with a Rigidbody (useGravity on, low damping, Interpolate) and colliders.
    /// Defaults aim for an aggressive Battlefield 6 Little Bird: snappy rotation, heavy momentum, ~300 km/h top speed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChopperAuthoring : MonoBehaviour
    {
        [Header("Weight and lift")]
        [SerializeField, Tooltip("Gravity multiplier. Above 1 = heavier; drops and climbs feel punchier.")]
        private float gravityScale = 1.6f;
        [SerializeField, Range(0f, 1.5f), Tooltip("Extra lift at full collective, as a fraction of hover lift.")]
        private float collectiveRange = 0.9f;
        [SerializeField, Range(0f, 1f), Tooltip("Lift grows with tilt to hold height. 1 = Battlefield: tilt is pure horizontal thrust.")]
        private float tiltCompensation = 0.9f;
        [SerializeField, Range(10f, 80f), Tooltip("Largest tilt (degrees) that is still compensated.")]
        private float maxCompensatedTiltDegrees = 60f;

        [Header("Rotation (degrees per second at full input)")]
        [SerializeField] private float maxPitchRate = 90f;
        [SerializeField] private float maxYawRate = 70f;
        [SerializeField] private float maxRollRate = 140f;
        [SerializeField, Tooltip("How fast rotation reaches the requested rate. Higher = snappier.")]
        private float turnResponse = 12f;

        [Header("Drag (local axes: x sideways, y vertical, z forward)")]
        [SerializeField] private Vector3 quadraticDrag = new(0.004f, 0.003f, 0.0018f);
        [SerializeField] private Vector3 linearDrag = new(0.15f, 0.15f, 0.02f);
        [SerializeField, Tooltip("Nose turns into the direction of travel at speed.")]
        private float weathervane = 0.01f;
        [SerializeField, Tooltip("Flight assist: levels the chopper when there is no pitch/roll input. 0 = off (Battlefield default).")]
        private float autoLevel;

        [Header("Dash")]
        [SerializeField] private float dashBoost = 0.6f;
        [SerializeField] private float dashCooldown = 2f;

        // TODO: Phase 4 replaces these with the chassis (health) and nose weapon module.
        [Header("Health")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField, Tooltip("Speed loss in one tick (m/s) that does no crash damage.")]
        private float safeSpeedLoss = 8f;
        [SerializeField, Tooltip("Crash damage per m/s of speed loss above the safe value.")]
        private float crashDamagePerSpeedLoss = 4f;

        [Header("Nose gun (hitscan)")]
        [SerializeField, Tooltip("Shots per minute.")] private float roundsPerMinute = 750f;
        [SerializeField] private float damagePerShot = 8f;
        [SerializeField] private float range = 600f;
        [SerializeField, Tooltip("Gun position in chopper space (z = forward).")]
        private Vector3 muzzleOffset = new(0f, -0.6f, 2.5f);

        [Header("Pylon rockets")]
        [SerializeField, Tooltip("Seconds between rockets.")] private float rocketInterval = 0.6f;
        [SerializeField] private float rocketSpeed = 140f;
        [SerializeField] private float rocketLifetime = 4f;
        [SerializeField] private float rocketDirectDamage = 30f;
        [SerializeField] private float rocketSplashDamage = 25f;
        [SerializeField] private float rocketSplashRadius = 8f;
        [SerializeField, Tooltip("Pylon position in chopper space.")]
        private Vector3 rocketMuzzleOffset = new(1.4f, -0.8f, 1.5f);

        private sealed class Baker : Baker<ChopperAuthoring>
        {
            public override void Bake(ChopperAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new ChopperFlightConfig
                {
                    GravityScale = authoring.gravityScale,
                    CollectiveRange = authoring.collectiveRange,
                    TiltCompensation = authoring.tiltCompensation,
                    MaxCompensatedTilt = math.cos(math.radians(authoring.maxCompensatedTiltDegrees)),
                    MaxTurnRate = math.radians(new float3(authoring.maxPitchRate, authoring.maxYawRate, authoring.maxRollRate)),
                    TurnResponse = authoring.turnResponse,
                    QuadraticDrag = authoring.quadraticDrag,
                    LinearDrag = authoring.linearDrag,
                    Weathervane = authoring.weathervane,
                    AutoLevel = authoring.autoLevel,
                    DashBoost = authoring.dashBoost,
                    DashCooldown = authoring.dashCooldown,
                });
                AddComponent<ChopperFlightState>(entity);
                AddComponent<ChopperInput>(entity);

                AddComponent(entity, new Health { Current = authoring.maxHealth, Max = authoring.maxHealth });
                AddBuffer<DamageEvent>(entity);
                AddComponent<LastAttacker>(entity);
                AddComponent(entity, new CrashDamageConfig
                {
                    SafeSpeedLoss = authoring.safeSpeedLoss,
                    DamagePerSpeedLoss = authoring.crashDamagePerSpeedLoss,
                });
                AddComponent<CrashSensor>(entity);
                AddComponent(entity, new HitscanWeaponConfig
                {
                    FireInterval = 60f / math.max(1f, authoring.roundsPerMinute),
                    Damage = authoring.damagePerShot,
                    Range = authoring.range,
                    MuzzleOffset = authoring.muzzleOffset,
                });
                AddComponent<HitscanWeaponState>(entity);
                AddComponent(entity, new RocketLauncherConfig
                {
                    FireInterval = authoring.rocketInterval,
                    Speed = authoring.rocketSpeed,
                    Lifetime = authoring.rocketLifetime,
                    DirectDamage = authoring.rocketDirectDamage,
                    SplashDamage = authoring.rocketSplashDamage,
                    SplashRadius = authoring.rocketSplashRadius,
                    MuzzleOffset = authoring.rocketMuzzleOffset,
                });
                AddComponent<RocketLauncherState>(entity);
            }
        }
    }
}
