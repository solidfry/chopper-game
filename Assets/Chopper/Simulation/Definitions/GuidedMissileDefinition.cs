using System.Collections.Generic;
using Chopper.Simulation.Combat;
using UnityEngine;

namespace Chopper.Simulation.Definitions
{
    /// <summary>
    /// Tuning for a guided pylon missile (heat seeker, air-to-air or air-to-ground). Baked to a
    /// <see cref="PylonWeapon"/> with behaviour Guided. The missile itself is a ghost prefab with
    /// <see cref="Combat.GuidedMissileAuthoring"/> (made by Chopper → Setup).
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Weapons/Guided Missile", fileName = "GuidedMissile")]
    public sealed class GuidedMissileDefinition : WeaponDefinition
    {
        [Header("Launch")]
        [SerializeField, Tooltip("Seconds between missiles.")] private float fireInterval = 2.5f;
        [SerializeField] private float speed = 160f;
        [SerializeField] private float lifetime = 6f;
        [SerializeField, Tooltip("Seconds before it steers, so it clears the launcher.")] private float armTime = 0.15f;
        [SerializeField, Tooltip("Missile ghost prefab (GhostAuthoringComponent + GuidedMissileAuthoring).")]
        private GameObject missilePrefab;

        [Header("Seeker")]
        [SerializeField] private SeekerType seeker = SeekerType.Heat;
        [SerializeField] private TargetClass targets = TargetClass.Air;
        [SerializeField, Tooltip("Half-angle of the lock cone, degrees.")] private float lockConeDegrees = 15f;
        [SerializeField] private float lockRange = 600f;
        [SerializeField, Tooltip("Seconds on target to lock.")] private float lockTime = 1.2f;
        [SerializeField, Tooltip("On: cannot fire without a lock (no dumb-fire).")] private bool requiresLock = true;

        [Header("Flight")]
        [SerializeField, Tooltip("Degrees per second.")] private float turnRate = 120f;
        [SerializeField, Tooltip("Lead on a moving target: 0 = pure pursuit, 3 = full lead.")] private float navigationGain = 3f;
        [SerializeField, Tooltip("Explodes this close to the target, metres.")] private float proximityFuse = 6f;

        [Header("Damage")]
        [SerializeField] private float directDamage = 45f;
        [SerializeField] private float splashDamage = 20f;
        [SerializeField] private float splashRadius = 6f;

        [Header("Salvo (swarm)")]
        [SerializeField, Range(1, 16), Tooltip("Missiles per trigger pull.")] private int salvoCount = 1;
        [SerializeField, Tooltip("Seconds between missiles of a salvo. At least one tick (1/60 s).")] private float salvoInterval = 0.06f;
        [SerializeField, Tooltip("Half-angle of the launch fan, degrees.")] private float salvoSpreadDegrees;
        [SerializeField, Range(0f, 1f), Tooltip("Part of a salvo that flares do not fool.")] private float flareResistance;

        [Header("Cluster (0 submunitions = normal missile)")]
        [SerializeField, Range(0, Combat.Cluster.MaxCount), Tooltip("Splits into this many submunitions; no proximity fuse then.")]
        private int submunitions;
        [SerializeField, Tooltip("Splits this close to the target, metres.")] private float splitDistance = 60f;
        [SerializeField, Tooltip("Splits this long after launch at the latest, seconds.")] private float splitTime = 1.8f;
        [SerializeField] private float submunitionSpeed = 90f;
        [SerializeField, Tooltip("Half-angle of the spread cone, degrees.")] private float spreadDegrees = 14f;
        [SerializeField] private float submunitionLifetime = 2.5f;
        [SerializeField] private float submunitionDirectDamage = 20f;
        [SerializeField] private float submunitionSplashDamage = 18f;
        [SerializeField] private float submunitionSplashRadius = 6f;

        public float FireInterval => fireInterval;
        public float Speed => speed;
        public float Lifetime => lifetime;
        public float ArmTime => armTime;
        public GameObject MissilePrefab => missilePrefab;
        public SeekerType Seeker => seeker;
        public TargetClass Targets => targets;
        public float LockConeCos => Mathf.Cos(lockConeDegrees * Mathf.Deg2Rad);
        public float LockRange => lockRange;
        public float LockTime => lockTime;
        public bool RequiresLock => requiresLock;
        public float TurnRateRadians => turnRate * Mathf.Deg2Rad;
        public float NavigationGain => navigationGain;
        public float ProximityFuse => proximityFuse;
        public float DirectDamage => directDamage;
        public float SplashDamage => splashDamage;
        public float SplashRadius => splashRadius;

        public byte SalvoCount => (byte)Mathf.Clamp(salvoCount, 1, 16);
        public float SalvoInterval => salvoInterval;
        public float SalvoConeCos => Mathf.Cos(salvoSpreadDegrees * Mathf.Deg2Rad);
        public float FlareResistance => flareResistance;

        public ClusterConfig Cluster => new()
        {
            Count = (byte)Mathf.Clamp(submunitions, 0, Combat.Cluster.MaxCount),
            SplitDistance = splitDistance,
            SplitTime = splitTime,
            Speed = submunitionSpeed,
            ConeCos = Mathf.Cos(spreadDegrees * Mathf.Deg2Rad),
            Lifetime = submunitionLifetime,
            DirectDamage = submunitionDirectDamage,
            SplashDamage = submunitionSplashDamage,
            SplashRadius = submunitionSplashRadius,
        };

        public override void CollectProblems(List<string> problems)
        {
            if (missilePrefab == null) problems.Add($"{name}: no missile ghost prefab.");
            else if (missilePrefab.GetComponent<GuidedMissileAuthoring>() == null)
                problems.Add($"{name}: missile prefab has no GuidedMissileAuthoring.");
            if (seeker != SeekerType.None && targets == TargetClass.None) problems.Add($"{name}: seeker has no target classes.");
            if (speed <= 0f || lifetime <= 0f || fireInterval <= 0f) problems.Add($"{name}: speed, lifetime and fire interval must be above 0.");
            if (salvoCount > 1 && salvoInterval < 1f / 60f)
                problems.Add($"{name}: salvo interval below one tick (1/60 s); missiles of one tick would mismatch their predicted spawns.");
            if (salvoCount > 1 && fireInterval < salvoCount * salvoInterval)
                problems.Add($"{name}: fire interval is shorter than the salvo; salvos would overlap.");
            if (submunitions > 0 && (submunitionSpeed <= 0f || submunitionLifetime <= 0f || splitTime <= 0f))
                problems.Add($"{name}: cluster submunition speed, lifetime and split time must be above 0.");
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(GameObject prefab, TargetClass targetClasses, bool needsLock, float cone, float range, float lockSeconds,
            float missileSpeed, float turnDegreesPerSecond, float gain, float fuse, float direct, float splash, float radius, float interval,
            float life)
        {
            missilePrefab = prefab;
            targets = targetClasses;
            requiresLock = needsLock;
            lockConeDegrees = cone;
            lockRange = range;
            lockTime = lockSeconds;
            speed = missileSpeed;
            turnRate = turnDegreesPerSecond;
            navigationGain = gain;
            proximityFuse = fuse;
            directDamage = direct;
            splashDamage = splash;
            splashRadius = radius;
            fireInterval = interval;
            lifetime = life;
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetSalvo(int count, float interval, float spreadDegrees, float resistance)
        {
            salvoCount = count;
            salvoInterval = interval;
            salvoSpreadDegrees = spreadDegrees;
            flareResistance = resistance;
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetCluster(int count, float distance, float time, float subSpeed, float spread, float subLifetime, float direct,
            float splash, float radius)
        {
            submunitions = count;
            splitDistance = distance;
            splitTime = time;
            submunitionSpeed = subSpeed;
            spreadDegrees = spread;
            submunitionLifetime = subLifetime;
            submunitionDirectDamage = direct;
            submunitionSplashDamage = splash;
            submunitionSplashRadius = radius;
        }
    }
}
