using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Simulation.Definitions
{
    /// <summary>Tuning for a fast nose gun (hitscan with lag compensation). Baked to <see cref="Combat.HitscanWeaponConfig"/>.</summary>
    [CreateAssetMenu(menuName = "Chopper/Weapons/Hitscan Weapon", fileName = "HitscanWeapon")]
    public sealed class HitscanWeaponDefinition : WeaponDefinition
    {
        [SerializeField] private float roundsPerMinute = 750f;
        [SerializeField] private float damagePerShot = 8f;
        [SerializeField] private float range = 600f;

        public float FireInterval => 60f / Mathf.Max(1f, roundsPerMinute);
        public float DamagePerShot => damagePerShot;
        public float Range => range;

        public override void CollectProblems(List<string> problems)
        {
            if (roundsPerMinute <= 0f) problems.Add($"{name}: rounds per minute must be above 0.");
            if (damagePerShot <= 0f) problems.Add($"{name}: damage per shot must be above 0.");
            if (range <= 0f) problems.Add($"{name}: range must be above 0.");
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(float rpm, float damage, float maxRange)
        {
            roundsPerMinute = rpm;
            damagePerShot = damage;
            range = maxRange;
        }
    }
}
