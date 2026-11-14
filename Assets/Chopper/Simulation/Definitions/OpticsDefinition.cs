using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Chopper.Simulation.Definitions
{
    /// <summary>
    /// A zoom sight (utility module). The Zoom key steps through <see cref="ZoomLevels"/>; the nose gun limits how far
    /// (<see cref="HitscanWeaponDefinition.MaxZoom"/>), so a strong scope pays off only on a long-range gun. Baked to
    /// <see cref="Combat.OpticsConfig"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Modules/Optics", fileName = "Optics")]
    public sealed class OpticsDefinition : ModuleAbilityDefinition
    {
        [SerializeField, Tooltip("Magnifications the Zoom key steps through, low to high (2 = 2x). At most 4.")]
        private float[] zoomLevels = { 2f, 4f };

        public IReadOnlyList<float> ZoomLevels => zoomLevels;

        /// <summary>The levels as a fixed list (at most <see cref="Combat.ZoomMath.MaxSteps"/>).</summary>
        public FixedList32Bytes<float> Levels()
        {
            var levels = new FixedList32Bytes<float>();
            for (var i = 0; zoomLevels != null && i < zoomLevels.Length && i < Combat.ZoomMath.MaxSteps; i++)
                levels.Add(zoomLevels[i]);
            return levels;
        }

        public override void CollectProblems(List<string> problems)
        {
            if (zoomLevels == null || zoomLevels.Length == 0)
            {
                problems.Add($"{name}: no zoom levels.");
                return;
            }
            if (zoomLevels.Length > Combat.ZoomMath.MaxSteps)
                problems.Add($"{name}: more than {Combat.ZoomMath.MaxSteps} zoom levels; the rest are not used.");
            for (var i = 0; i < zoomLevels.Length; i++)
            {
                if (zoomLevels[i] < Combat.ZoomMath.MinMagnification)
                    problems.Add($"{name}: zoom level {i} must be at least {Combat.ZoomMath.MinMagnification}.");
                if (i > 0 && zoomLevels[i] <= zoomLevels[i - 1])
                    problems.Add($"{name}: zoom levels must go from low to high.");
            }
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(params float[] levels) => zoomLevels = levels;
    }
}
