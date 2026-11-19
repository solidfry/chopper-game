using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Mathematics;

namespace Chopper.Tests.EditMode
{
    /// <summary>Blast push and the heavy wreck parts (Docs/ChopperDeathDesign.md step 3).</summary>
    public class BlastPushTests
    {
        [Test]
        public void PushGrowsWithDamageAndIsCapped()
        {
            Assert.AreEqual(0f, Explosions.PushSpeed(-5f));
            Assert.AreEqual(25f * Explosions.PushPerDamage, Explosions.PushSpeed(25f), 1e-4f);
            Assert.AreEqual(Explosions.MaxPush, Explosions.PushSpeed(1000f));
        }

        [Test]
        public void OffCentrePushTumblesWithinTheCap()
        {
            Assert.AreEqual(0f, math.length(BlastPushSystem.Spin(float3.zero, new float3(10f, 0f, 0f))), 1e-5f);
            var spin = BlastPushSystem.Spin(new float3(0f, 1f, 0f), new float3(5f, 0f, 0f));
            Assert.Less(spin.z, 0f, "a push to the right above the centre rolls it right");
            Assert.LessOrEqual(math.length(BlastPushSystem.Spin(new float3(0f, 9f, 0f), new float3(50f, 0f, 0f))), BlastPushSystem.MaxSpin + 1e-4f);
        }

        [Test]
        public void PartsLaunchTheSameFromTheSameSeedAndSpreadOut()
        {
            var wreck = new float3(0f, -4f, 10f);
            WreckParts.Launch(1234u, 1, 3, wreck, out var offsetA, out var velocityA);
            WreckParts.Launch(1234u, 1, 3, wreck, out var offsetB, out var velocityB);
            Assert.AreEqual(velocityA, velocityB);
            Assert.AreEqual(offsetA, offsetB);

            WreckParts.Launch(1234u, 0, 3, float3.zero, out var first, out _);
            WreckParts.Launch(1234u, 2, 3, float3.zero, out var third, out _);
            Assert.Greater(math.distance(first.xz, third.xz), 0.5f, "parts go different ways");
            Assert.Greater(first.y, 0f, "parts are thrown up");
        }

        [Test]
        public void PartDamageNeedsSpeed()
        {
            Assert.AreEqual(0f, WreckParts.HitDamage(30f, WreckParts.HarmlessSpeed - 0.1f));
            Assert.AreEqual(30f, WreckParts.HitDamage(30f, 40f));
            Assert.AreEqual(15f, WreckParts.HitDamage(30f, WreckParts.FullDamageSpeed * 0.5f), 1e-4f);
        }

        [Test]
        public void PartBouncesLowAndSlides()
        {
            var after = WreckParts.Bounce(new float3(10f, -10f, 0f), new float3(0f, 1f, 0f));
            Assert.AreEqual(3f, after.y, 1e-4f);
            Assert.AreEqual(5.5f, after.x, 1e-4f);
        }
    }
}
