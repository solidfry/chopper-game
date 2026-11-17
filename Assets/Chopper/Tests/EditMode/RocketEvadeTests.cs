using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Mathematics;

namespace Chopper.Tests
{
    public sealed class RocketEvadeTests
    {
        private static readonly float3 Target = new(0f, 100f, 100f);

        [Test]
        public void Threatens_OnlyARoundAimedAtTheTarget()
        {
            var from = new float3(0f, 100f, 0f);
            Assert.That(RocketEvade.Threatens(from, new float3(0f, 0f, 200f), Target, 0f, 6f), Is.True, "flying straight at it");
            Assert.That(RocketEvade.Threatens(from, new float3(200f, 0f, 0f), Target, 0f, 6f), Is.False, "flying sideways");
            Assert.That(RocketEvade.Threatens(from - new float3(0f, 0f, 500f), new float3(0f, 0f, 200f), Target, 0f, 6f), Is.False, "out of range");
        }

        [Test]
        public void Threatens_AHangingMineOnlyWhenTheTargetIsClose()
        {
            Assert.That(RocketEvade.Threatens(Target + new float3(10f, 0f, 0f), float3.zero, Target, 8f, 10f), Is.True);
            Assert.That(RocketEvade.Threatens(Target + new float3(60f, 0f, 0f), float3.zero, Target, 8f, 10f), Is.False);
        }

        [Test]
        public void PassedBy_NearMissThatFliesOnCountsAFarOneDoesNot()
        {
            Assert.That(RocketEvade.PassedBy(Target + new float3(4f, 0f, -5f), Target + new float3(4f, 0f, 5f), Target), Is.True);
            Assert.That(RocketEvade.PassedBy(Target + new float3(40f, 0f, -5f), Target + new float3(40f, 0f, 5f), Target), Is.False, "too far");
            Assert.That(RocketEvade.PassedBy(Target + new float3(4f, 0f, -15f), Target + new float3(4f, 0f, -6f), Target), Is.False, "still coming");
        }

        [Test]
        public void BurstMissed_OnlyJustOutsideTheSplash()
        {
            Assert.That(RocketEvade.BurstMissed(Target + new float3(5f, 0f, 0f), Target, 6f), Is.False, "inside the splash: hit");
            Assert.That(RocketEvade.BurstMissed(Target + new float3(12f, 0f, 0f), Target, 6f), Is.True);
            Assert.That(RocketEvade.BurstMissed(Target + new float3(80f, 0f, 0f), Target, 6f), Is.False, "nowhere near");
        }

        [Test]
        public void LeftBehind_AMineYouGotClearOf()
        {
            Assert.That(RocketEvade.LeftBehind(Target + new float3(40f, 0f, 0f), float3.zero, Target, 8f, 10f), Is.True);
            Assert.That(RocketEvade.LeftBehind(Target + new float3(20f, 0f, 0f), float3.zero, Target, 8f, 10f), Is.False);
            Assert.That(RocketEvade.LeftBehind(Target + new float3(40f, 0f, 0f), new float3(0f, 0f, 100f), Target, 8f, 10f), Is.False, "a moving rocket");
        }
    }
}
