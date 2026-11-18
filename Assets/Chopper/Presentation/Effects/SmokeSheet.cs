using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// The shared smoke and dust flipbook (Assets/Chopper/Data/Effects/SmokeSheet.png, made by the editor setup):
    /// <see cref="Tiles"/> x <see cref="Tiles"/> frames; each row is a different puff, each column an older stage of
    /// it. <see cref="Apply"/> sets a particle system to pick a random row and play it once over each particle's life.
    /// </summary>
    public static class SmokeSheet
    {
        public const int Tiles = 4;

        public static void Apply(ParticleSystem system)
        {
            var sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = Tiles;
            sheet.numTilesY = Tiles;
            sheet.animation = ParticleSystemAnimationType.SingleRow;
            sheet.rowMode = ParticleSystemAnimationRowMode.Random;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            sheet.cycleCount = 1;
        }
    }
}
