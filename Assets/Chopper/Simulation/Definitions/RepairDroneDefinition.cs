using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Simulation.Definitions
{
    /// <summary>
    /// Repair drone on a pylon (owner, 17 Nov): pylon fire sends it to its target (now the own chopper; allies when teams
    /// exist), it mends <see cref="HealShare"/> of the max health over <see cref="HealSeconds"/> and flies back. Damage does
    /// not stop it (the field repair stops on any hit). The magazine is the drone count; the reload is its cooldown.
    /// Baked to a <see cref="Combat.PylonWeapon"/> with <see cref="Combat.PylonBehaviour.Drone"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Weapons/Repair Drone", fileName = "RepairDrone")]
    public sealed class RepairDroneDefinition : WeaponDefinition
    {
        [SerializeField, Range(0.05f, 1f), Tooltip("Share of the target's max health mended per trip.")]
        private float healShare = 0.4f;
        [SerializeField, Tooltip("Seconds the drone heals at the target.")]
        private float healSeconds = 6f;
        [SerializeField, Tooltip("Drone flight speed, m/s (to an ally and back).")]
        private float speed = 40f;
        [SerializeField, Range(1, 4), Tooltip("Drones before a reload.")]
        private int magazineSize = 1;
        [SerializeField, Tooltip("Seconds after the drone leaves until the next one is ready.")]
        private float reloadSeconds = 30f;

        public float HealShare => healShare;
        public float HealSeconds => healSeconds;
        public float Speed => speed;
        public byte MagazineSize => (byte)Mathf.Clamp(magazineSize, 1, 4);
        public float ReloadSeconds => reloadSeconds;

        public override void CollectProblems(List<string> problems)
        {
            base.CollectProblems(problems);
            if (healSeconds <= 0f) problems.Add($"{name}: heal seconds must be above 0.");
            if (speed <= 0f) problems.Add($"{name}: speed must be above 0.");
            if (reloadSeconds <= healSeconds) problems.Add($"{name}: reload must be longer than the heal, or a second drone is ready while the first heals.");
        }
    }
}
