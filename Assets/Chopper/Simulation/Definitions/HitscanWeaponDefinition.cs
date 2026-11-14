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
        [SerializeField, Tooltip("Damage starts to fall at this distance, metres. 0 = full damage to the range.")]
        private float falloffStart;
        [SerializeField, Range(0f, 1f), Tooltip("Part of the damage left at full range.")] private float falloffFloor = 0.5f;

        [Header("Heat")]
        [SerializeField, Tooltip("Seconds of steady fire until it overheats. 0 = no heat limit.")] private float heatSeconds = 6f;
        [SerializeField, Tooltip("Seconds to cool from full heat to cold. An overheated gun fires again at 40% heat.")]
        private float coolSeconds = 4f;

        public float FireInterval => 60f / Mathf.Max(1f, roundsPerMinute);
        public float DamagePerShot => damagePerShot;
        public float Range => range;
        public float FalloffStart => falloffStart;
        public float FalloffFloor => falloffFloor;
        /// <summary>Damage per second of steady fire, at close range.</summary>
        public float DamagePerSecond => damagePerShot / FireInterval;
        public float HeatPerShot => heatSeconds > 0f ? FireInterval / heatSeconds : 0f;
        public float CoolRate => coolSeconds > 0f ? 1f / coolSeconds : 0f;

        public override void CollectProblems(List<string> problems)
        {
            if (roundsPerMinute <= 0f) problems.Add($"{name}: rounds per minute must be above 0.");
            if (damagePerShot <= 0f) problems.Add($"{name}: damage per shot must be above 0.");
            if (range <= 0f) problems.Add($"{name}: range must be above 0.");
            if (falloffStart < 0f || (falloffStart > 0f && falloffStart >= range))
                problems.Add($"{name}: falloff start must be 0 (off) or below the range.");
            if (heatSeconds > 0f && coolSeconds <= 0f) problems.Add($"{name}: a gun with a heat limit must have a cool time above 0.");
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(float rpm, float damage, float maxRange)
        {
            roundsPerMinute = rpm;
            damagePerShot = damage;
            range = maxRange;
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetFalloff(float start, float floor)
        {
            falloffStart = start;
            falloffFloor = floor;
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetHeat(float secondsToOverheat, float secondsToCool)
        {
            heatSeconds = secondsToOverheat;
            coolSeconds = secondsToCool;
        }
    }
}
