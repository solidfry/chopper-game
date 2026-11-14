using Unity.Collections;
using Unity.Mathematics;

namespace Chopper.Simulation.Session
{
    /// <summary>What the server knows about one spawn point at the moment of a spawn.</summary>
    public struct SpawnCandidate
    {
        /// <summary><see cref="SpawnPoint.Index"/>.</summary>
        public int Index;
        /// <summary>Distance to the nearest level geometry, m (measured up to the preferred clearance).</summary>
        public float WallDistance;
        /// <summary>Height over the ground, m. Infinity = no ground found below.</summary>
        public float GroundHeight;
        public bool OpenSky;
        public bool InBounds;
        /// <summary>Distance to the nearest live enemy, m. Infinity = none.</summary>
        public float EnemyDistance;
        /// <summary>Distance to the nearest live chopper of any side, or to a point taken in this tick, m.</summary>
        public float ChopperDistance;
        /// <summary>An enemy in range has a clear line to the point.</summary>
        public bool InEnemySight;
        public float SecondsSinceUse;

        /// <summary>A point with nothing wrong (tests start from this).</summary>
        public static SpawnCandidate Clear(int index) => new()
        {
            Index = index,
            WallDistance = float.PositiveInfinity,
            GroundHeight = float.PositiveInfinity,
            OpenSky = true,
            InBounds = true,
            EnemyDistance = float.PositiveInfinity,
            ChopperDistance = float.PositiveInfinity,
            InEnemySight = false,
            SecondsSinceUse = float.PositiveInfinity,
        };
    }

    public enum SpawnPick : byte
    {
        /// <summary>No usable point: the caller uses the spawner's own point.</summary>
        None,
        /// <summary>Every rule holds.</summary>
        Safe,
        /// <summary>Clear of walls and choppers, but an enemy is inside the minimum distance.</summary>
        Relaxed,
    }

    /// <summary>
    /// Picks the spawn point (no world access, so tests can call it). Rules, in order:
    /// <list type="number">
    /// <item>A point is never used when it is blocked (wall too near, too low or too high, no open sky, outside the
    /// arena) or occupied (a chopper inside <see cref="SpawnRules.OccupiedRadius"/>).</item>
    /// <item>Among the points with no enemy inside <see cref="SpawnRules.MinEnemyDistance"/>, the highest
    /// <see cref="Score"/> wins.</item>
    /// <item>If there is none, the enemy distance rule is dropped (the farthest from enemies then scores best).</item>
    /// <item>Ties: the point not used for the longest time, then the lowest <see cref="SpawnCandidate.Index"/>.</item>
    /// </list>
    /// </summary>
    public static class SpawnScoring
    {
        private const float ScoreEpsilon = 1e-4f;

        public static bool IsBlocked(in SpawnCandidate c, in SpawnRules rules)
        {
            if (!c.InBounds) return true;
            if (c.WallDistance < rules.WallClearance) return true;
            if (c.GroundHeight < rules.MinGroundHeight) return true;
            if (rules.MaxGroundHeight > 0f && !float.IsPositiveInfinity(c.GroundHeight) && c.GroundHeight > rules.MaxGroundHeight) return true;
            return rules.NeedOpenSky && !c.OpenSky;
        }

        public static bool IsOccupied(in SpawnCandidate c, in SpawnRules rules) => c.ChopperDistance < rules.OccupiedRadius;

        /// <summary>
        /// Higher is better. <paramref name="relaxed"/>: the enemy distance counts from 0 m (not from the minimum), so
        /// that points inside the minimum distance still differ.
        /// </summary>
        public static float Score(in SpawnCandidate c, in SpawnRules rules, bool relaxed = false)
        {
            var from = relaxed ? 0f : rules.MinEnemyDistance;
            var enemy = Share(c.EnemyDistance, from, rules.PreferredEnemyDistance);
            var clearance = Share(c.WallDistance, rules.WallClearance, rules.PreferredClearance);
            var sight = c.InEnemySight ? 0f : 1f;
            var recent = rules.ReuseSeconds > 0f ? math.saturate(1f - c.SecondsSinceUse / rules.ReuseSeconds) : 0f;
            return rules.EnemyDistanceWeight * enemy
                   + rules.OutOfSightWeight * sight
                   + rules.ClearanceWeight * clearance
                   - rules.ReuseWeight * recent;
        }

        /// <summary>Position in <paramref name="candidates"/> of the point to use; -1 when <see cref="SpawnPick.None"/>.</summary>
        public static int Pick(NativeArray<SpawnCandidate> candidates, in SpawnRules rules, out SpawnPick pick)
        {
            var best = Best(candidates, rules, relaxed: false);
            if (best >= 0)
            {
                pick = SpawnPick.Safe;
                return best;
            }
            best = Best(candidates, rules, relaxed: true);
            pick = best >= 0 ? SpawnPick.Relaxed : SpawnPick.None;
            return best;
        }

        private static int Best(NativeArray<SpawnCandidate> candidates, in SpawnRules rules, bool relaxed)
        {
            var best = -1;
            var bestScore = 0f;
            for (var i = 0; i < candidates.Length; i++)
            {
                var c = candidates[i];
                if (IsBlocked(c, rules) || IsOccupied(c, rules)) continue;
                if (!relaxed && c.EnemyDistance < rules.MinEnemyDistance) continue;
                var score = Score(c, rules, relaxed);
                if (best < 0 || Better(score, c, bestScore, candidates[best]))
                {
                    best = i;
                    bestScore = score;
                }
            }
            return best;
        }

        private static bool Better(float score, in SpawnCandidate c, float bestScore, in SpawnCandidate best)
        {
            if (score > bestScore + ScoreEpsilon) return true;
            if (score < bestScore - ScoreEpsilon) return false;
            if (c.SecondsSinceUse != best.SecondsSinceUse) return c.SecondsSinceUse > best.SecondsSinceUse;
            return c.Index < best.Index;
        }

        /// <summary>0 at <paramref name="from"/> or less, 1 at <paramref name="to"/> or more (infinity = 1).</summary>
        private static float Share(float value, float from, float to)
        {
            if (value >= to) return 1f;
            return to > from ? math.saturate((value - from) / (to - from)) : 0f;
        }
    }
}
