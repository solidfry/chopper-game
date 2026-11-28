using Chopper.Simulation.Visuals;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// What a rocket, bomb or mine blast throws up from the surface it hits (Docs/WeaponFeelPlan.md step 3, "impacts by
    /// surface"), on top of the family's own blast. Pure data, emitted by <see cref="DamageParticles.EmitSurfaceBlast"/>:
    /// ground = a dust plume and dark clods; concrete = grey dust, chips and a few sparks; metal = a shower of sparks and
    /// a little dark smoke; water = a white column of spray and mist. Counts grow with the blast radius (the standard 8 m pod = 1).
    /// </summary>
    public readonly struct SurfaceBlast
    {
        /// <summary>Blast radius that gives scale 1 (the standard rocket pod).</summary>
        public const float StandardRadius = 8f;

        public readonly int Dust;
        public readonly int Spray;
        public readonly int Sparks;
        /// <summary>Spray launch speed, m/s (scaled).</summary>
        public readonly float SpraySpeed;
        /// <summary>0 = straight along the surface normal, 1 = a wide fan.</summary>
        public readonly float SpraySpread;
        /// <summary>Spray size, m (scaled).</summary>
        public readonly float SpraySize;
        /// <summary>Dust and spray colours. <see cref="UseMapDust"/>: the dust is the map's sand or ash instead.</summary>
        public readonly Color DustColour;
        public readonly Color SprayColour;
        public readonly bool UseMapDust;
        /// <summary>Size and speed factor from the blast radius.</summary>
        public readonly float Scale;

        private SurfaceBlast(int dust, int spray, int sparks, float spraySpeed, float spraySpread, float spraySize, Color dustColour,
            Color sprayColour, bool useMapDust, float scale)
        {
            Dust = dust;
            Spray = spray;
            Sparks = sparks;
            SpraySpeed = spraySpeed;
            SpraySpread = spraySpread;
            SpraySize = spraySize;
            DustColour = dustColour;
            SprayColour = sprayColour;
            UseMapDust = useMapDust;
            Scale = scale;
        }

        /// <summary>Size factor for a blast radius: 0.5 (small) to 2.5 (bomb); 1 for the standard pod or no radius.</summary>
        public static float ScaleFor(float splashRadius) =>
            splashRadius > 0f ? Mathf.Clamp(splashRadius / StandardRadius, 0.5f, 2.5f) : 1f;

        /// <summary>The surface's throw-up for a blast of <paramref name="splashRadius"/> metres.</summary>
        public static SurfaceBlast For(SurfaceKind surface, float splashRadius)
        {
            var scale = ScaleFor(splashRadius);
            // Counts grow with the blast's area, not its radius, but capped: a bomb must not empty the particle budget.
            var count = Mathf.Min(scale * scale, 3f);
            var speed = Mathf.Sqrt(scale);
            return surface switch
            {
                SurfaceKind.Ground => new SurfaceBlast(N(14, count), N(36, count), 0, 22f * speed, 0.55f, 0.35f * speed,
                    new Color(0.55f, 0.48f, 0.38f, 0.8f), new Color(0.22f, 0.18f, 0.14f, 1f), true, scale),
                SurfaceKind.Water => new SurfaceBlast(N(12, count), N(70, count), 0, 26f * speed, 0.3f, 0.45f * speed,
                    new Color(0.86f, 0.9f, 0.93f, 0.7f), new Color(0.9f, 0.95f, 1f, 0.9f), false, scale),
                SurfaceKind.Metal => new SurfaceBlast(N(4, count), N(10, count), N(110, count), 16f * speed, 0.8f, 0.12f * speed,
                    new Color(0.3f, 0.3f, 0.32f, 0.6f), new Color(0.18f, 0.18f, 0.2f, 1f), false, scale),
                _ => new SurfaceBlast(N(18, count), N(30, count), N(16, count), 20f * speed, 0.65f, 0.22f * speed,
                    new Color(0.62f, 0.6f, 0.57f, 0.8f), new Color(0.48f, 0.46f, 0.43f, 1f), false, scale),
            };
        }

        private static int N(int baseCount, float factor) => Mathf.RoundToInt(baseCount * factor);
    }
}
