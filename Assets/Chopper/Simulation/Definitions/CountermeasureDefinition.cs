using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Simulation.Definitions
{
    /// <summary>Flares: break heat locks and pull heat seekers off. Baked to <see cref="Combat.CountermeasureConfig"/>.</summary>
    [CreateAssetMenu(menuName = "Chopper/Abilities/Countermeasure", fileName = "Countermeasure")]
    public sealed class CountermeasureDefinition : ModuleAbilityDefinition
    {
        [SerializeField, Tooltip("Seconds between drops.")] private float cooldown = 8f;
        [SerializeField, Tooltip("Seconds the chopper cannot be heat-locked after a drop.")] private float decoyDuration = 2f;

        public float Cooldown => cooldown;
        public float DecoyDuration => decoyDuration;

        public override void CollectProblems(List<string> problems)
        {
            if (cooldown <= 0f) problems.Add($"{name}: cooldown must be above 0.");
            if (decoyDuration <= 0f) problems.Add($"{name}: decoy duration must be above 0.");
        }
    }
}
