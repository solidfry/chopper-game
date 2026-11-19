using Chopper.Presentation.Effects;
using NUnit.Framework;
using UnityEngine;

namespace Chopper.Tests
{
    public sealed class DebrisMotionTests
    {
        [Test]
        public void Launch_KeepsTheWrecksVelocity_AndSmallPiecesFlyFaster()
        {
            var wreck = new Vector3(0f, -10f, 30f);
            var big = DebrisMotion.Launch(wreck, Vector3.right, 3f, 0.5f);
            var small = DebrisMotion.Launch(wreck, Vector3.right, 0.5f, 0.5f);
            Assert.That(big.z, Is.EqualTo(wreck.z * DebrisMotion.Inherit).Within(1e-3f));
            Assert.That((small - wreck * DebrisMotion.Inherit).magnitude, Is.GreaterThan((big - wreck * DebrisMotion.Inherit).magnitude));
            Assert.That(small.x, Is.GreaterThan(0f), "out from the blast");
        }

        [Test]
        public void Bounce_SlowHitsRest_FastHitsRebound()
        {
            var fast = DebrisMotion.Bounce(new Vector3(4f, -20f, 0f), Vector3.up, out var restFast);
            Assert.That(restFast, Is.False);
            Assert.That(fast.y, Is.GreaterThan(0f));
            Assert.That(fast.x, Is.LessThan(4f), "friction");

            DebrisMotion.Bounce(new Vector3(1f, -0.5f, 0f), Vector3.up, out var restSlow);
            Assert.That(restSlow, Is.True);
        }

        [Test]
        public void Trails_FireGoesOut_SmokeThins()
        {
            Assert.That(DebrisMotion.Heat(0f, 4f), Is.EqualTo(1f));
            Assert.That(DebrisMotion.Heat(4f, 4f), Is.EqualTo(0f));
            Assert.That(DebrisMotion.Heat(1f, 0f), Is.EqualTo(0f));
            Assert.That(DebrisMotion.Smoke(0f, 8f), Is.GreaterThan(DebrisMotion.Smoke(6f, 8f)));
            Assert.That(DebrisMotion.Smoke(8f, 8f), Is.EqualTo(0f));
        }
    }
}
