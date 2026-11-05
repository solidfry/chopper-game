using System.Collections.Generic;
using Chopper.Simulation.Combat;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// How a damaged chopper looks, as plain maths (no Unity objects), so tests can check it. Used by
    /// <see cref="DamageEffectsSystem"/>.
    /// </summary>
    public static class DamageLook
    {
        /// <summary>A chopper keeps this many hit points for its smoke; a new hit replaces the oldest.</summary>
        public const int MaxPoints = 3;
        /// <summary>A hit nearer than this (m) to a kept point moves that point instead of adding one.</summary>
        public const float MergeDistance = 0.8f;

        /// <summary>Smoke strength 0..1: 0 at <paramref name="smokeBelow"/> health share and above, 1 at zero health.</summary>
        public static float Smoke(float healthShare, float smokeBelow)
        {
            if (smokeBelow <= 0f) return 0f;
            return Mathf.Clamp01(1f - healthShare / smokeBelow);
        }

        /// <summary>Smoke colour: light grey at the first smoke, black from <paramref name="heavyBelow"/> down.</summary>
        public static Color SmokeColour(float healthShare, float smokeBelow, float heavyBelow, Color light, Color heavy)
        {
            if (smokeBelow <= heavyBelow) return heavy;
            var t = Mathf.Clamp01((smokeBelow - healthShare) / (smokeBelow - heavyBelow));
            return Color.Lerp(light, heavy, t);
        }

        /// <summary>Fire strength 0..1: 0 at <paramref name="fireBelow"/> health share and above, 1 at zero health.</summary>
        public static float Fire(float healthShare, float fireBelow) => Smoke(healthShare, fireBelow);

        /// <summary>Sparks for one hit: more for bigger hits and blasts and crashes, fewer for a hard landing.</summary>
        public static int Sparks(float damage, DamageKind kind, float perDamage, Vector2Int range)
        {
            var scale = kind switch
            {
                DamageKind.Explosion => 1.5f,
                DamageKind.Impact => 1.5f,
                DamageKind.Landing => 0.6f,
                _ => 1f,
            };
            return Mathf.Clamp(Mathf.RoundToInt(damage * perDamage * scale), range.x, range.y);
        }

        /// <summary>Hits that leave a smoking hole. A hard landing hurts the skids and the frame: no hole.</summary>
        public static bool LeavesMark(DamageKind kind) => kind is DamageKind.Bullet or DamageKind.Explosion or DamageKind.Impact;

        /// <summary>
        /// Keeps a hit point (local space) in <paramref name="points"/>: moves a near one, else adds it, else replaces
        /// the oldest (the list is oldest first).
        /// </summary>
        public static void AddPoint(List<(Vector3 Point, Vector3 Normal)> points, Vector3 point, Vector3 normal)
        {
            for (var i = 0; i < points.Count; i++)
            {
                if ((points[i].Point - point).sqrMagnitude > MergeDistance * MergeDistance) continue;
                points.RemoveAt(i);
                break;
            }
            if (points.Count >= MaxPoints) points.RemoveAt(0);
            points.Add((point, normal));
        }
    }
}
