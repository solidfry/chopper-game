using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Editor only. Finds Capture the Nuke sites on a map (owner, 20 Nov: "each map should have good positions for the team
    /// bases"): the pad near the middle, the two bases on opposite sides, each on open, flat ground with clear sky above
    /// (physics rays on the open scenes). The bases stay out of the pad turrets' reach.
    /// </summary>
    internal static class NukeSiteFinder
    {
        /// <summary>Clear, flat radius a base needs: the 3 x 4 spawn grid (16 m apart) and room for the base model.</summary>
        public const float BaseRadius = 45f;
        /// <summary>Clear radius the pad needs: its turret ring (2 x 22 m) and room to hover.</summary>
        public const float PadRadius = 55f;
        /// <summary>Base to pad, at least: turret range (260 m) + ring (44 m) + room to spawn and climb.</summary>
        public const float MinBaseToPad = 500f;
        /// <summary>Highest step allowed inside a site, metres.</summary>
        private const float MaxStep = 1.5f;
        /// <summary>Clear air above a site, metres.</summary>
        private const float ClearHeight = 60f;

        /// <summary>A site may sit this far above the street level (kerbs, plazas), metres.</summary>
        private const float FloorTolerance = 6f;
        private static float _floor;
        private static Vector3 _centre;
        /// <summary>Sites stay this far inside the arena edge (half size minus room), x and z.</summary>
        private static Vector2 _inner = new(float.MaxValue, float.MaxValue);

        public struct Sites
        {
            public Vector3 Pad;
            public Vector3 Base1;
            public Vector3 Base2;
        }

        /// <summary>
        /// The best sites inside the arena (centre, half size). False when no layout fits (then the caller keeps its
        /// defaults). Tries base axes every 15°, bases at 55..80 % of the way to the edge.
        /// </summary>
        public static bool Find(Vector3 centre, Vector3 halfSize, out Sites sites)
        {
            sites = default;
            Physics.SyncTransforms();
            var top = centre.y + halfSize.y;
            // The street level: the most common ground height (1 m steps) in a coarse sweep. Sites must be on it, not on
            // roofs (the lowest hit is no good: water and tunnels sit below the streets).
            _floor = float.NaN;
            var counts = new System.Collections.Generic.Dictionary<int, int>();
            for (var x = -halfSize.x; x <= halfSize.x; x += 50f)
            for (var z = -halfSize.z; z <= halfSize.z; z += 50f)
            {
                if (!Physics.Raycast(new Vector3(centre.x + x, top, centre.z + z), Vector3.down, out var info, top + 2000f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                var level = Mathf.RoundToInt(info.point.y);
                counts[level] = counts.TryGetValue(level, out var c) ? c + 1 : 1;
            }
            var most = 0;
            foreach (var pair in counts)
                if (pair.Value > most) { most = pair.Value; _floor = pair.Key; }
            if (float.IsNaN(_floor)) return false;

            _centre = centre;
            _inner = new Vector2(halfSize.x, halfSize.z) - Vector2.one * (BaseRadius + 60f);
            // The pad: as near the middle as the map allows (a dense centre pushes it out).
            var pad = Vector3.zero;
            var padFound = false;
            foreach (var search in new[] { 350f, 650f, 950f })
                if (BestNear(centre, search, 30f, PadRadius, top, out pad)) { padFound = true; break; }
            if (!padFound) return false;

            var reach = Mathf.Min(halfSize.x, halfSize.z);
            var bestScore = float.MinValue;
            var found = false;
            for (var angle = 0; angle < 180; angle += 15)
            {
                var axis = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                for (var share = 0.55f; share <= 0.81f; share += 0.0833f)
                {
                    var target1 = centre + axis * (reach * share);
                    var target2 = centre - axis * (reach * share);
                    if (!BestNear(target1, 220f, 40f, BaseRadius, top, out var base1)) continue;
                    if (!BestNear(target2, 220f, 40f, BaseRadius, top, out var base2)) continue;
                    var d1 = Flat(base1 - pad).magnitude;
                    var d2 = Flat(base2 - pad).magnitude;
                    if (d1 < MinBaseToPad || d2 < MinBaseToPad) continue;
                    // Fair (same distance to the pad) and long enough to be a trip, not a sprint.
                    var score = -Mathf.Abs(d1 - d2) * 4f + Mathf.Min(d1, d2) * 0.2f;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    sites = new Sites { Pad = pad, Base1 = base1, Base2 = base2 };
                    found = true;
                }
            }
            return found;
        }

        /// <summary>The usable site closest to <paramref name="target"/> on a grid within <paramref name="search"/> metres.</summary>
        private static bool BestNear(Vector3 target, float search, float step, float radius, float top, out Vector3 site)
        {
            site = default;
            var best = float.MaxValue;
            for (var x = -search; x <= search; x += step)
            for (var z = -search; z <= search; z += step)
            {
                var offset = new Vector3(x, 0f, z);
                if (offset.sqrMagnitude > search * search) continue;
                var at = target + offset;
                if (Mathf.Abs(at.x - _centre.x) > _inner.x || Mathf.Abs(at.z - _centre.z) > _inner.y) continue;
                if (!Usable(target + offset, radius, top, out var ground)) continue;
                var distance = offset.sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                site = ground;
            }
            return best < float.MaxValue;
        }

        /// <summary>Flat ground (9 rays within <paramref name="radius"/>) with nothing in the air above it.</summary>
        public static bool Usable(Vector3 point, float radius, float top, out Vector3 ground)
        {
            ground = default;
            if (!Ground(point, top, out var centre)) return false;
            if (Mathf.Abs(centre.y - _floor) > FloorTolerance) return false;
            var low = centre.y;
            var high = centre.y;
            for (var i = 0; i < 8; i++)
            {
                var angle = i * Mathf.PI / 4f;
                var at = point + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                if (!Ground(at, top, out var hit)) return false;
                low = Mathf.Min(low, hit.y);
                high = Mathf.Max(high, hit.y);
            }
            if (high - low > MaxStep) return false;
            var box = new Vector3(radius, ClearHeight * 0.5f, radius);
            if (Physics.CheckBox(new Vector3(point.x, high + MaxStep + 1f + ClearHeight * 0.5f, point.z), box, Quaternion.identity,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            ground = new Vector3(point.x, centre.y, point.z);
            return true;
        }

        private static bool Ground(Vector3 point, float top, out Vector3 hit)
        {
            hit = default;
            if (!Physics.Raycast(new Vector3(point.x, top, point.z), Vector3.down, out var info, top + 2000f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore)) return false;
            if (info.normal.y < 0.95f) return false;
            hit = info.point;
            return true;
        }

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    }
}
