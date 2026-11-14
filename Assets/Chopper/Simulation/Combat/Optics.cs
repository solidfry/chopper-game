using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// The zoom steps of this chopper's optics module, already limited by its gun (<see cref="ZoomMath.Steps"/>).
    /// Empty = no zoom (no optics module). Set from the loadout on client and server; only the owner's client uses it
    /// (camera and aim speed). Not a ghost field: every world builds it from the same loadout.
    /// </summary>
    public struct OpticsConfig : IComponentData
    {
        /// <summary>Magnifications, low to high (2 = "2x").</summary>
        public FixedList32Bytes<float> Steps;
    }

    /// <summary>Zoom maths shared by the loadout builder, the camera, the input and tests. Pure, Burst friendly.</summary>
    public static class ZoomMath
    {
        /// <summary>Most zoom steps of one optics module.</summary>
        public const int MaxSteps = 4;
        /// <summary>A step must magnify at least this much, or it is not worth a key press.</summary>
        public const float MinMagnification = 1.2f;
        /// <summary>The highest magnification with no gun (rockets only).</summary>
        public const float NoGunMaxZoom = 2f;

        /// <summary>
        /// The steps a player gets: each level of the optics module, capped at what the gun supports. Levels that end
        /// the same (or too low) are dropped, so a strong scope on a short-range gun gives fewer steps.
        /// </summary>
        public static FixedList32Bytes<float> Steps(in FixedList32Bytes<float> levels, float gunMaxZoom)
        {
            var steps = new FixedList32Bytes<float>();
            var last = 1f;
            for (var i = 0; i < levels.Length && steps.Length < MaxSteps; i++)
            {
                var level = math.min(levels[i], gunMaxZoom);
                if (level < MinMagnification || level <= last + 0.05f) continue;
                steps.Add(level);
                last = level;
            }
            return steps;
        }

        /// <summary>The step after a key press: 0 (no zoom) → 1 → ... → <paramref name="stepCount"/> → 0.</summary>
        public static int Next(int step, int stepCount) => stepCount <= 0 ? 0 : (step + 1) % (stepCount + 1);

        /// <summary>Magnification of <paramref name="step"/> (0 = no zoom = 1).</summary>
        public static float Magnification(in FixedList32Bytes<float> steps, int step) =>
            step <= 0 || step > steps.Length ? 1f : steps[step - 1];

        /// <summary>The vertical field of view (degrees) that shows the scene <paramref name="magnification"/> times larger.</summary>
        public static float FieldOfView(float baseDegrees, float magnification)
        {
            if (magnification <= 1f) return baseDegrees;
            var half = math.radians(math.clamp(baseDegrees, 1f, 179f)) * 0.5f;
            return math.degrees(2f * math.atan(math.tan(half) / magnification));
        }

        /// <summary>Mouse turn speed while zoomed: the picture moves as fast as without zoom.</summary>
        public static float MouseScale(float magnification) => 1f / math.max(magnification, 1f);

        /// <summary>Key and stick turn speed while zoomed: slower for fine aim, but not as slow as the mouse (a stick at
        /// full push must still turn the chopper).</summary>
        public static float AxisScale(float magnification) => 1f / math.sqrt(math.max(magnification, 1f));
    }
}
