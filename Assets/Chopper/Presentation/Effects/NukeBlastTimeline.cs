using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// The nuke blast over time (<see cref="NukeBlastSystem"/>; prefab parts by Editor/Setup/NukeBlastAssets), in metres
    /// for a cap of <see cref="BaseRadius"/> (the system scales the whole blast) and seconds since the flash:
    /// fireball (0-1 s), rise into the mushroom (eased, most of it in <c>riseSeconds</c>), cooling cracks, shock rings
    /// round the cap, wind strokes and dust puffs running out on the ground, and the dissolve at the end. Pure: tests.
    /// </summary>
    public static class NukeBlastTimeline
    {
        /// <summary>Cap radius (m) the prefab is built for.</summary>
        public const float BaseRadius = 55f;
        public const int RingCount = 3;
        public const int PuffCount = 16;
        public const int SwirlCount = 3;
        public const float RingLife = 2.6f;
        public const float SwirlLife = 3.4f;
        /// <summary>The burn mark stays this long after the cloud has dissolved.</summary>
        public const float ScorchLinger = 8f;

        /// <summary>Height of the cap centre above the ground (m).</summary>
        public static float CapHeight(float t, float burst, float top, float riseSeconds) =>
            burst + (top - burst) * (1f - Mathf.Exp(-3f * Mathf.Max(0f, t) / Mathf.Max(riseSeconds, 0.1f)));

        /// <summary>Cap radius: the fireball swells to most of its size in under half a second (owner: "expand wider sooner").</summary>
        public static float CapRadius(float t) => BaseRadius * (0.45f + 0.55f * (1f - Mathf.Exp(-Mathf.Max(0f, t) / 0.3f)));

        /// <summary>Extra width of the cap (x and z) as it rises and flattens into the mushroom head.</summary>
        public static float CapSpread(float t) => 1f + 0.45f * (1f - Mathf.Exp(-Mathf.Max(0f, t) / 1.5f));

        /// <summary>Height of the stem: always up to the underside of the cap, so the two stay joined.</summary>
        public static float StemHeight(float capHeight, float capRadius) => Mathf.Max(0.01f, capHeight - capRadius * 0.4f);

        /// <summary>0..1: white-hot for the first moment, then the cracks cool and narrow.</summary>
        public static float CapHeat(float t) => t < 0.8f ? 1f : 0.25f + 0.75f * Mathf.Exp(-(t - 0.8f) / 8f);

        /// <summary>0..1: the stem draws up under the rising cap.</summary>
        public static float StemGrow(float t) => 0.25f + 0.75f * Smooth((t - 0.1f) / 1.5f);

        public static float StemHeat(float t) => 0.08f + 0.6f * Mathf.Exp(-Mathf.Max(0f, t) / 5f);

        /// <summary>Shock ring <paramref name="index"/> round the cap: false when it is not showing.</summary>
        public static bool Ring(int index, float t, out float radius, out float fade)
        {
            var age = t - (0.15f + index * 0.35f);
            radius = 0f;
            fade = 1f;
            if (age < 0f || age >= RingLife) return false;
            var k = age / RingLife;
            radius = BaseRadius * (1.15f + 3.4f * EaseOut(k));
            fade = Mathf.Pow(k, 1.3f);
            return true;
        }

        /// <summary>Wind stroke <paramref name="index"/> on the ground: radius, fade and a spin (degrees).</summary>
        public static bool Swirl(int index, float t, out float radius, out float fade, out float spin)
        {
            var age = t - (0.2f + index * 0.18f);
            radius = 0f;
            fade = 1f;
            spin = (index % 2 == 0 ? 1f : -1f) * 40f * Mathf.Max(0f, t) + index * 50f;
            if (age < 0f || age >= SwirlLife) return false;
            var k = age / SwirlLife;
            radius = 30f + (BaseRadius * 8f - 30f) * EaseOut(k);
            fade = Mathf.Pow(k, 1.2f);
            return true;
        }

        /// <summary>Dust puff <paramref name="index"/>: distance out from the centre, size (radius) and heat.</summary>
        public static void Puff(int index, float t, out float distance, out float size, out float heat)
        {
            var age = Mathf.Max(0f, t - 0.25f);
            var reach = BaseRadius * (1.5f + 0.7f * Hash(index));
            distance = 15f + (reach - 15f) * EaseOut(Mathf.Clamp01(age / 3f));
            size = BaseRadius * (0.3f + 0.25f * Hash(index + 31)) * Smooth(age / 0.8f);
            heat = 0.05f + 0.85f * Mathf.Exp(-age / 2f);
        }

        /// <summary>Angle (degrees) of puff <paramref name="index"/> round the centre.</summary>
        public static float PuffAngle(int index) => index * 360f / PuffCount + (Hash(index + 7) - 0.5f) * 14f;

        /// <summary>Burn mark half-width (m) and its glow.</summary>
        public static void Scorch(float t, out float size, out float heat)
        {
            size = BaseRadius * 2.6f * Smooth((t - 0.15f) / 1f);
            heat = Mathf.Exp(-Mathf.Max(0f, t) / 4f);
        }

        /// <summary>0..1 dissolve of the cloud parts after <paramref name="endSeconds"/>.</summary>
        public static float Fade(float t, float endSeconds, float fadeSeconds) => Mathf.Clamp01((t - endSeconds) / Mathf.Max(fadeSeconds, 0.1f));

        /// <summary>Seconds until nothing is left (the burn mark goes last).</summary>
        public static float Total(float endSeconds, float fadeSeconds) => endSeconds + fadeSeconds + ScorchLinger;

        /// <summary>0..1 screen flash: up in 0.06 s, then a fast fade with a long tail.</summary>
        public static float ScreenFlash(float t) => t < 0f ? 0f : t < 0.06f ? t / 0.06f : Mathf.Exp(-(t - 0.06f) / 0.5f);

        /// <summary>Seconds until the shock front reaches <paramref name="distance"/> metres.</summary>
        public static float ShockArrival(float distance, float speed) => distance / Mathf.Max(speed, 1f);

        private static float EaseOut(float k) => 1f - (1f - k) * (1f - k) * (1f - k);

        private static float Smooth(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3f - 2f * k);
        }

        /// <summary>A fixed 0..1 value per index (puffs differ, but the same every blast).</summary>
        private static float Hash(int index) => Mathf.Repeat(Mathf.Sin(index * 12.9898f + 4.1414f) * 43758.5453f, 1f);
    }
}
