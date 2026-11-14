using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Simulation.Definitions
{
    /// <summary>
    /// Tuning for unguided pylon weapons: rockets, salvo pods, flak rockets (proximity fuse), bombs (gravity) and air
    /// mines (drag). One behaviour, different numbers. Baked to a <see cref="Combat.PylonWeapon"/> (behaviour Rocket).
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Weapons/Rocket Weapon", fileName = "RocketWeapon")]
    public sealed class RocketWeaponDefinition : WeaponDefinition
    {
        [SerializeField, Tooltip("Seconds between rockets.")] private float fireInterval = 0.6f;
        [SerializeField] private float speed = 140f;
        [SerializeField] private float lifetime = 4f;
        [SerializeField] private float directDamage = 30f;
        [SerializeField] private float splashDamage = 25f;
        [SerializeField] private float splashRadius = 8f;
        [SerializeField, Range(0, 255), Tooltip("Rockets before a reload. 0 = no limit.")] private int magazineSize = 14;
        [SerializeField, Tooltip("Seconds after the last rocket until the magazine is full again.")] private float reloadSeconds = 6f;

        [Header("Path")]
        [SerializeField, Tooltip("Downward acceleration, m/s2. 0 = flies straight. Bombs: about 25.")] private float gravity;
        [SerializeField, Tooltip("Speed lost per second, as a rate. 0 = keeps its speed. Air mines: about 2 (they stop and hang).")]
        private float drag;

        [Header("Proximity fuse")]
        [SerializeField, Tooltip("Explodes this close to a target, metres (splash damage only). 0 = contact only.")]
        private float proximityFuse;
        [SerializeField, Tooltip("What sets off the fuse.")] private Combat.TargetClass fuseTargets = Combat.TargetClass.Air;

        [Header("Salvo")]
        [SerializeField, Range(1, 16), Tooltip("Rockets per trigger pull. A salvo is one round of the magazine.")] private int salvoCount = 1;
        [SerializeField, Tooltip("Seconds between rockets of a salvo. At least one tick (1/60 s).")] private float salvoInterval = 0.07f;
        [SerializeField, Tooltip("Half-angle of the launch fan, degrees.")] private float salvoSpreadDegrees;

        public float Gravity => Mathf.Max(0f, gravity);
        public float Drag => Mathf.Max(0f, drag);
        public float ProximityFuse => Mathf.Max(0f, proximityFuse);
        public Combat.TargetClass FuseTargets => proximityFuse > 0f ? fuseTargets : Combat.TargetClass.None;
        public byte SalvoCount => (byte)Mathf.Clamp(salvoCount, 1, 16);
        public float SalvoInterval => Mathf.Max(salvoInterval, 1f / 60f);
        public float SalvoConeCos => Mathf.Cos(Mathf.Deg2Rad * Mathf.Clamp(salvoSpreadDegrees, 0f, 89f));

        public byte MagazineSize => (byte)Mathf.Clamp(magazineSize, 0, 255);
        public float ReloadSeconds => reloadSeconds;
        public float FireInterval => fireInterval;
        public float Speed => speed;
        public float Lifetime => lifetime;
        public float DirectDamage => directDamage;
        public float SplashDamage => splashDamage;
        public float SplashRadius => splashRadius;

        public override void CollectProblems(List<string> problems)
        {
            if (fireInterval <= 0f) problems.Add($"{name}: fire interval must be above 0.");
            if (speed <= 0f) problems.Add($"{name}: speed must be above 0.");
            if (lifetime <= 0f) problems.Add($"{name}: lifetime must be above 0.");
            if (magazineSize > 0 && reloadSeconds <= fireInterval)
                problems.Add($"{name}: reload must be longer than the fire interval, or the magazine never empties.");
            if (salvoCount > 1 && fireInterval <= (salvoCount - 1) * SalvoInterval)
                problems.Add($"{name}: the fire interval must be longer than one salvo ({(salvoCount - 1) * SalvoInterval:0.00} s).");
            if (proximityFuse > 0f && fuseTargets == Combat.TargetClass.None)
                problems.Add($"{name}: a proximity fuse needs a target class.");
            if (proximityFuse > splashRadius && proximityFuse > 0f)
                problems.Add($"{name}: the proximity fuse is wider than the splash radius, so a fuse burst does no damage.");
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(float interval, float rocketSpeed, float life, float direct, float splash, float radius, int magazine,
            float reload)
        {
            fireInterval = interval;
            speed = rocketSpeed;
            lifetime = life;
            directDamage = direct;
            splashDamage = splash;
            splashRadius = radius;
            magazineSize = magazine;
            reloadSeconds = reload;
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetPath(float gravityAcceleration, float dragRate)
        {
            gravity = gravityAcceleration;
            drag = dragRate;
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetFuse(float radius, Combat.TargetClass targets)
        {
            proximityFuse = radius;
            fuseTargets = targets;
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetSalvo(int count, float interval, float spreadDegrees)
        {
            salvoCount = count;
            salvoInterval = interval;
            salvoSpreadDegrees = spreadDegrees;
        }
    }
}
