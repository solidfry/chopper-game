using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Simulation.Definitions
{
    /// <summary>Flares: break heat locks and pull heat seekers off. Baked to <see cref="Combat.CountermeasureConfig"/>.</summary>
    [CreateAssetMenu(menuName = "Chopper/Abilities/Countermeasure", fileName = "Countermeasure")]
    public sealed class CountermeasureDefinition : ModuleAbilityDefinition
    {
        [SerializeField, Tooltip("Seconds between drops.")] private float cooldown = 3f;
        [SerializeField, Tooltip("Seconds the chopper cannot be heat-locked after a drop.")] private float decoyDuration = 2f;
        [SerializeField, Range(0, 255), Tooltip("Drops before a reload. 0 = no limit.")] private int magazineSize = 3;
        [SerializeField, Tooltip("Seconds after the last drop until the magazine is full again.")] private float reloadSeconds = 18f;
        [SerializeField, Tooltip("When a bot drops flares. Auto = about 1 s before a missile hits.")] private Bots.BotUse bot;

        public float Cooldown => cooldown;
        public float DecoyDuration => decoyDuration;
        public byte MagazineSize => (byte)Mathf.Clamp(magazineSize, 0, 255);
        public float ReloadSeconds => reloadSeconds;
        public Bots.BotUse Bot => bot;

        public override void CollectProblems(List<string> problems)
        {
            if (cooldown <= 0f) problems.Add($"{name}: cooldown must be above 0.");
            if (decoyDuration <= 0f) problems.Add($"{name}: decoy duration must be above 0.");
            if (magazineSize > 0 && reloadSeconds <= cooldown)
                problems.Add($"{name}: reload must be longer than the cooldown, or the magazine never empties.");
        }
    }
}
