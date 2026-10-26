using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Simulation.Definitions
{
    /// <summary>Tuning for unguided pylon rockets. Baked to <see cref="Combat.RocketLauncherConfig"/>.</summary>
    [CreateAssetMenu(menuName = "Chopper/Weapons/Rocket Weapon", fileName = "RocketWeapon")]
    public sealed class RocketWeaponDefinition : WeaponDefinition
    {
        [SerializeField, Tooltip("Seconds between rockets.")] private float fireInterval = 0.6f;
        [SerializeField] private float speed = 140f;
        [SerializeField] private float lifetime = 4f;
        [SerializeField] private float directDamage = 30f;
        [SerializeField] private float splashDamage = 25f;
        [SerializeField] private float splashRadius = 8f;

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
        }
    }
}
