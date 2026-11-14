using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Chopper.Simulation.Combat
{
    /// <summary>A special view of an optics module. Local picture only: the simulation does not change.</summary>
    public enum VisionMode : byte
    {
        None = 0,
        /// <summary>Amplified light: a bright green picture in the dark, with grain.</summary>
        Night = 1,
        /// <summary>Heat: a cold grey world, hot things glow (engines, missiles). A fast or boosting chopper is brighter.</summary>
        Thermal = 2,
        /// <summary>Kinetic tracker: only what moves glows. A hovering chopper is not seen.</summary>
        Motion = 3,
        /// <summary>Deep scan: targets glow through walls, at short range, on a dark and noisy picture.</summary>
        DeepScan = 4,
    }

    /// <summary>
    /// The optics module of this chopper: zoom steps, already limited by its gun (<see cref="ZoomMath.Steps"/>), and
    /// its special view. No steps and no vision = no optics module. Set from the loadout on client and server; only the owner's client uses it
    /// (camera and aim speed). Not a ghost field: every world builds it from the same loadout.
    /// </summary>
    public struct OpticsConfig : IComponentData
    {
        /// <summary>Magnifications, low to high (2 = "2x").</summary>
        public FixedList32Bytes<float> Steps;
        public VisionMode Vision;
        /// <summary>The special view marks targets to this distance, metres. 0 = no limit.</summary>
        public float VisionRange;
    }

    /// <summary>What the special views show. Pure, shared by the presentation and tests.</summary>
    public static class VisionMath
    {
        /// <summary>Heat (<see cref="Seeker.Heat"/>) that glows at full strength on thermal.</summary>
        public const float FullHeat = 2f;
        /// <summary>The kinetic tracker sees nothing slower than this, m/s.</summary>
        public const float MotionMinSpeed = 6f;
        /// <summary>Speed that glows at full strength on the kinetic tracker, m/s.</summary>
        public const float MotionFullSpeed = 40f;
        /// <summary>The glow fades over this last part of the range.</summary>
        public const float RangeFade = 0.15f;
        /// <summary>Heat of a missile in flight (its motor burns).</summary>
        public const float MissileHeat = 2.5f;
        /// <summary>Heat of a rocket in flight.</summary>
        public const float RocketHeat = 2f;
        /// <summary>Heat of a fresh explosion; it cools over <see cref="BlastSeconds"/>.</summary>
        public const float BlastHeat = 3f;
        public const float BlastSeconds = 1.5f;
        /// <summary>Heat of a burning flare (hotter than any engine: that is how it fools a seeker).</summary>
        public const float FlareHeat = 4f;

        public static bool MarksTargets(VisionMode mode) => mode is VisionMode.Thermal or VisionMode.Motion or VisionMode.DeepScan;

        /// <summary>How bright a target glows in <paramref name="mode"/>, 0..1. 0 = not shown.</summary>
        /// <param name="range">The module's range; 0 = no limit.</param>
        public static float Signal(VisionMode mode, float heat, float speed, float distance, float range)
        {
            var edge = 1f;
            if (range > 0f)
            {
                if (distance >= range) return 0f;
                edge = math.saturate((range - distance) / (range * RangeFade));
            }

            switch (mode)
            {
                case VisionMode.Thermal: return math.saturate(heat / FullHeat) * edge;
                case VisionMode.Motion: return math.saturate((speed - MotionMinSpeed) / (MotionFullSpeed - MotionMinSpeed)) * edge;
                case VisionMode.DeepScan: return edge;
                default: return 0f;
            }
        }
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
