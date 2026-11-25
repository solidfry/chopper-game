using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Editor only. Finds Capture the Nuke sites on a map (owner, 20 Nov: "each map should have good positions for the team
    /// bases"): the pad near the middle, the two bases on opposite sides, each on open, flat ground with clear sky above
    /// (physics rays on the open scenes). The bases stay out of the pad turrets' reach.
    /// One search = one instance (<see cref="Find"/>): the street level, arena and lava lines are per search, not static.
    /// </summary>
    internal sealed class NukeSiteFinder
    {
        /// <summary>Clear, flat radius round a base's dock (the first test; <see cref="Footprint"/> then checks the
        /// whole facility).</summary>
        public const float BaseRadius = 70f;
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
        private float _floor;
        private Vector3 _centre;
        /// <summary>Sites stay this far inside the arena edge (half size minus room), x and z.</summary>
        private Vector2 _inner = new(float.MaxValue, float.MaxValue);

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
        /// <param name="lava">Lava lines to stay off (points, width); null for none.</param>
        public static bool Find(Vector3 centre, Vector3 halfSize,
            System.Collections.Generic.IEnumerable<(Vector3[] Points, float Width)> lava, out Sites sites, Vector3? fixedPad = null)
            => new NukeSiteFinder(lava).Search(centre, halfSize, out sites, fixedPad);

        private NukeSiteFinder(System.Collections.Generic.IEnumerable<(Vector3[] Points, float Width)> lava)
        {
            if (lava == null) return;
            foreach (var (points, width) in lava)
                for (var i = 0; i + 1 < points.Length; i++)
                    _lava.Add((points[i], points[i + 1], width * 0.5f + LavaClearance));
        }

        private bool Search(Vector3 centre, Vector3 halfSize, out Sites sites, Vector3? fixedPad)
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
            if (fixedPad.HasValue)
            {
                // The pad (and its turrets) stays; only the bases move.
                pad = fixedPad.Value;
                padFound = true;
            }
            else
                foreach (var search in new[] { 350f, 650f, 950f })
                    if (BestNear(centre, search, 30f, PadRadius, top, out pad)) { padFound = true; break; }
            if (!padFound) return false;

            var reach = Mathf.Min(halfSize.x, halfSize.z);
            var bestScore = float.MinValue;
            var found = false;
            for (var angle = 0; angle < 180; angle += 15)
            {
                var axis = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                for (var share = 0.4f; share <= 0.81f; share += 0.0667f)
                {
                    var target1 = centre + axis * (reach * share);
                    var target2 = centre - axis * (reach * share);
                    if (!BestNear(target1, 260f, 30f, BaseRadius, top, out var base1, pad)) continue;
                    if (!BestNear(target2, 260f, 30f, BaseRadius, top, out var base2, pad)) continue;
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
        private bool BestNear(Vector3 target, float search, float step, float radius, float top, out Vector3 site,
            Vector3? facePad = null)
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
                if (NearLava(target + offset, radius)) continue;
                if (!Usable(target + offset, radius, top, out var ground)) continue;
                if (facePad.HasValue)
                {
                    if (!Footprint(ground, facePad.Value - ground, top, out var height)) continue;
                    ground.y = height;
                }
                var distance = offset.sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                site = ground;
            }
            return best < float.MaxValue;
        }

        /// <summary>Flat ground (9 rays within <paramref name="radius"/>) with nothing in the air above it.</summary>
        private bool Usable(Vector3 point, float radius, float top, out Vector3 ground)
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


        /// <summary>The base model's footprint around its origin (PartForge team_base facility, 25 Nov: plinth 200 x 132 m,
        /// centre 6 m back), metres: x half width, z from the front edge (towards the pad) to the back edge.</summary>
        public const float FootHalfWidth = 101f, FootFront = 61f, FootBack = -73f;
        /// <summary>Highest step allowed under the base model, metres (the apron is 0.3 m thick).</summary>
        private const float FootStep = 1f;
        /// <summary>Sites stay this far from the edge of any lava line, metres.</summary>
        private const float LavaClearance = 40f;
        /// <summary>Lava lines to stay off: segments with the half width plus <see cref="LavaClearance"/>.</summary>
        private readonly System.Collections.Generic.List<(Vector3 A, Vector3 B, float Keep)> _lava = new();

        private bool NearLava(Vector3 p, float pad)
        {
            foreach (var (a, b, keep) in _lava)
            {
                var ab = new Vector2(b.x - a.x, b.z - a.z);
                var ap = new Vector2(p.x - a.x, p.z - a.z);
                var t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
                if ((ap - ab * t).magnitude < keep + pad) return true;
            }
            return false;
        }

        /// <summary>
        /// The whole base model fits here facing <paramref name="towardsPad"/>: ground within <see cref="FootStep"/>, no
        /// lava, clear air. <paramref name="height"/> = the highest ground under it (the model sits there).
        /// </summary>
        private bool Footprint(Vector3 origin, Vector3 towardsPad, float top, out float height)
        {
            height = 0f;
            var forward = new Vector3(towardsPad.x, 0f, towardsPad.z).normalized;
            if (forward == Vector3.zero) forward = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            float low = float.MaxValue, high = float.MinValue;
            for (var x = -FootHalfWidth; x <= FootHalfWidth + 0.1f; x += FootHalfWidth / 8f)
            for (var z = FootBack; z <= FootFront + 0.1f; z += (FootFront - FootBack) / 10f)
            {
                var p = origin + right * x + forward * z;
                if (NearLava(p, 0f)) return false;
                if (!Ground(p, top, out var g)) return false;
                if (Mathf.Abs(g.y - _floor) > FloorTolerance) return false;
                low = Mathf.Min(low, g.y);
                high = Mathf.Max(high, g.y);
            }
            if (high - low > FootStep) return false;
            var centre = origin + forward * ((FootFront + FootBack) * 0.5f);
            var half = new Vector3(FootHalfWidth, ClearHeight * 0.5f, (FootFront - FootBack) * 0.5f);
            if (Physics.CheckBox(new Vector3(centre.x, high + 1.5f + ClearHeight * 0.5f, centre.z), half,
                    Quaternion.LookRotation(forward), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            height = high;
            return true;
        }

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    }
}
