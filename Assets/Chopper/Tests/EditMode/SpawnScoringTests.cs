using Chopper.Simulation.Session;
using NUnit.Framework;
using Unity.Collections;

namespace Chopper.Tests
{
    /// <summary><see cref="SpawnScoring"/> picks from hand-made points (no world).</summary>
    public sealed class SpawnScoringTests
    {
        private static readonly SpawnRules Rules = SpawnRules.Default;

        private static int Pick(out SpawnPick pick, params SpawnCandidate[] candidates)
        {
            using var array = new NativeArray<SpawnCandidate>(candidates, Allocator.Temp);
            return SpawnScoring.Pick(array, Rules, out pick);
        }

        private static SpawnCandidate Point(int index, float enemy = float.PositiveInfinity)
        {
            var c = SpawnCandidate.Clear(index);
            c.EnemyDistance = enemy;
            c.ChopperDistance = enemy;
            return c;
        }

        // -- distance to enemies ------------------------------------------------------------------------------------

        [Test]
        public void FarFromEnemy_Wins()
        {
            var best = Pick(out var pick, Point(0, 200f), Point(1, 500f), Point(2, 300f));
            Assert.AreEqual(1, best);
            Assert.AreEqual(SpawnPick.Safe, pick);
        }

        [Test]
        public void EnemyInsideMinimum_IsNotUsed_WhenAnotherPointIsSafe()
        {
            // Point 0 is perfect but for the enemy 100 m away; point 1 is poor (seen, just used) but safe.
            var near = Point(0, Rules.MinEnemyDistance - 50f);
            var poor = Point(1, Rules.MinEnemyDistance + 1f);
            poor.InEnemySight = true;
            poor.SecondsSinceUse = 0f;
            var best = Pick(out var pick, near, poor);
            Assert.AreEqual(1, best);
            Assert.AreEqual(SpawnPick.Safe, pick);
        }

        [Test]
        public void EnemyBeyondPreferredDistance_AddsNoScore()
        {
            var a = Point(0, Rules.PreferredEnemyDistance);
            var b = Point(1, Rules.PreferredEnemyDistance * 3f);
            Assert.AreEqual(SpawnScoring.Score(a, Rules), SpawnScoring.Score(b, Rules), 1e-5f);
        }

        [Test]
        public void OutOfSight_BeatsInSight_AtTheSameDistance()
        {
            var seen = Point(0, 400f);
            seen.InEnemySight = true;
            Assert.AreEqual(1, Pick(out _, seen, Point(1, 400f)));
        }

        [Test]
        public void EveryPointNearAnEnemy_TakesTheFarthest_AsRelaxed()
        {
            var best = Pick(out var pick, Point(0, 40f), Point(1, 120f), Point(2, 80f));
            Assert.AreEqual(1, best);
            Assert.AreEqual(SpawnPick.Relaxed, pick);
        }

        [Test]
        public void OccupiedPoint_IsNeverUsed()
        {
            var occupied = Point(0, 500f);
            occupied.ChopperDistance = Rules.OccupiedRadius - 1f; // a friend sits on it
            var best = Pick(out var pick, occupied, Point(1, 60f));
            Assert.AreEqual(1, best);
            Assert.AreEqual(SpawnPick.Relaxed, pick);
        }

        // -- wall clearance -----------------------------------------------------------------------------------------

        [Test]
        public void WallInsideClearance_Blocks()
        {
            var c = Point(0);
            c.WallDistance = Rules.WallClearance - 0.5f;
            Assert.IsTrue(SpawnScoring.IsBlocked(c, Rules));
            c.WallDistance = Rules.WallClearance;
            Assert.IsFalse(SpawnScoring.IsBlocked(c, Rules));
        }

        [Test]
        public void MoreFreeSpace_Wins_UpToThePreferredClearance()
        {
            var tight = Point(0, 400f);
            tight.WallDistance = Rules.WallClearance + 1f;
            var open = Point(1, 400f);
            open.WallDistance = Rules.PreferredClearance;
            var wide = Point(2, 400f);
            wide.WallDistance = Rules.PreferredClearance * 4f;
            Assert.AreEqual(1, Pick(out _, tight, open));
            Assert.AreEqual(SpawnScoring.Score(open, Rules), SpawnScoring.Score(wide, Rules), 1e-5f);
        }

        [Test]
        public void NoOpenSky_Blocks()
        {
            var c = Point(0);
            c.OpenSky = false;
            Assert.IsTrue(SpawnScoring.IsBlocked(c, Rules));
        }

        [Test]
        public void OutsideTheArena_Blocks()
        {
            var c = Point(0);
            c.InBounds = false;
            Assert.IsTrue(SpawnScoring.IsBlocked(c, Rules));
        }

        // -- height -------------------------------------------------------------------------------------------------

        [Test]
        public void TooLow_Blocks()
        {
            var c = Point(0);
            c.GroundHeight = Rules.MinGroundHeight - 1f;
            Assert.IsTrue(SpawnScoring.IsBlocked(c, Rules));
            c.GroundHeight = Rules.MinGroundHeight;
            Assert.IsFalse(SpawnScoring.IsBlocked(c, Rules));
        }

        [Test]
        public void TooHigh_Blocks_OnlyWithALimit()
        {
            var c = Point(0);
            c.GroundHeight = 500f;
            Assert.IsFalse(SpawnScoring.IsBlocked(c, Rules), "no limit by default");

            var limited = Rules;
            limited.MaxGroundHeight = 200f;
            Assert.IsTrue(SpawnScoring.IsBlocked(c, limited));
            c.GroundHeight = float.PositiveInfinity; // no ground found below: not too high
            Assert.IsFalse(SpawnScoring.IsBlocked(c, limited));
        }

        // -- ties and no safe point ---------------------------------------------------------------------------------

        [Test]
        public void Tie_TakesTheLeastRecentlyUsed_ThenTheLowestIndex()
        {
            // Both past the reuse time, so the score is the same; point 5 was used longer ago.
            var recent = Point(2);
            recent.SecondsSinceUse = Rules.ReuseSeconds + 5f;
            var old = Point(5);
            old.SecondsSinceUse = Rules.ReuseSeconds + 60f;
            Assert.AreEqual(1, Pick(out _, recent, old));

            // Never used, same score: the lowest index, in any order of the list.
            Assert.AreEqual(1, Pick(out _, Point(7), Point(3), Point(4)));
        }

        [Test]
        public void JustUsedPoint_LosesToAnEqualPoint()
        {
            var used = Point(0, 400f);
            used.SecondsSinceUse = 0f;
            Assert.AreEqual(1, Pick(out _, used, Point(1, 400f)));
        }

        [Test]
        public void SamePick_EveryTime()
        {
            var first = Pick(out _, Point(0, 300f), Point(1, 450f), Point(2, 450f));
            for (var i = 0; i < 10; i++)
                Assert.AreEqual(first, Pick(out _, Point(0, 300f), Point(1, 450f), Point(2, 450f)));
            Assert.AreEqual(1, first);
        }

        [Test]
        public void NoUsablePoint_GivesNone()
        {
            var wall = Point(0);
            wall.WallDistance = 0f;
            var low = Point(1);
            low.GroundHeight = 2f;
            var taken = Point(2);
            taken.ChopperDistance = 0f;
            Assert.AreEqual(-1, Pick(out var pick, wall, low, taken));
            Assert.AreEqual(SpawnPick.None, pick);

            Assert.AreEqual(-1, Pick(out pick));
            Assert.AreEqual(SpawnPick.None, pick);
        }
    }
}
