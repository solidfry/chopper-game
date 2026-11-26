using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Mathematics;

namespace Chopper.Tests
{
    public class MissileArcTests
    {
        private static MissileArc Arc => new()
        {
            KickRadians = math.radians(35f),
            StartSpeedShare = 0.45f,
            BoostSeconds = 0.7f,
            WeaveRadians = math.radians(10f),
            WeaveHz = 0.8f,
        };

        private static float Degrees(float3 a, float3 b) => math.degrees(math.acos(math.clamp(math.dot(math.normalize(a), math.normalize(b)), -1f, 1f)));

        [Test]
        public void KickDirection_TurnsUpAndToThePylonSide()
        {
            var forward = new float3(0f, 0f, 1f);
            var right = MissileArcMath.KickDirection(forward, math.up(), math.right(), 1f, Arc.KickRadians, 0);
            var left = MissileArcMath.KickDirection(forward, math.up(), math.right(), -1f, Arc.KickRadians, 0);
            Assert.That(Degrees(right, forward), Is.EqualTo(35f).Within(0.1f));
            Assert.That(right.x, Is.GreaterThan(0f), "Right pylon: out to the right.");
            Assert.That(left.x, Is.LessThan(0f), "Left pylon: out to the left.");
            Assert.That(right.y, Is.GreaterThan(0f), "Always up a little.");
            Assert.That(MissileArcMath.KickDirection(forward, math.up(), math.right(), 1f, 0f, 0), Is.EqualTo(forward), "No kick: straight.");
        }

        [Test]
        public void KickDirection_SalvoShotsFanOutButStayAboveTheNose()
        {
            var forward = new float3(0f, 0f, 1f);
            foreach (var side in new[] { -1f, 0f, 1f })
                for (var shot = 0; shot < 10; shot++)
                {
                    var direction = MissileArcMath.KickDirection(forward, math.up(), math.right(), side, Arc.KickRadians, shot);
                    Assert.That(Degrees(direction, forward), Is.EqualTo(35f).Within(0.1f), $"Side {side} shot {shot}: the kick angle.");
                    Assert.That(direction.y, Is.GreaterThan(0.05f), $"Side {side} shot {shot}: never down into the ground.");
                }
        }

        [Test]
        public void SpeedAt_RisesFromTheStartShareToFullSpeed()
        {
            Assert.That(MissileArcMath.SpeedAt(Arc, 200f, 0f), Is.EqualTo(90f).Within(0.01f));
            Assert.That(MissileArcMath.SpeedAt(Arc, 200f, 0.35f), Is.InRange(100f, 190f));
            Assert.That(MissileArcMath.SpeedAt(Arc, 200f, 0.7f), Is.EqualTo(200f).Within(0.01f));
            Assert.That(MissileArcMath.SpeedAt(Arc, 200f, 5f), Is.EqualTo(200f).Within(0.01f));
            Assert.That(MissileArcMath.SpeedAt(default, 200f, 0f), Is.EqualTo(200f), "No arc: full speed at once.");
        }

        [Test]
        public void WeaveAim_CirclesTheTargetFarOutAndHomesStraightNearIn()
        {
            var target = new float3(0f, 100f, 0f);
            var far = new float3(0f, 100f, -400f);
            var aim = MissileArcMath.WeaveAim(Arc, far, target, 0.3f, 0f);
            var radius = math.distance(aim, target);
            Assert.That(radius, Is.EqualTo(math.tan(Arc.WeaveRadians) * 400f).Within(0.5f), "Far out: the full corkscrew.");
            Assert.That(math.abs(math.dot(math.normalize(aim - target), math.normalize(target - far))), Is.LessThan(1e-3f), "Square to the line of sight.");

            var near = new float3(0f, 100f, -20f);
            Assert.That(math.distance(MissileArcMath.WeaveAim(Arc, near, target, 0.3f, 0f), target), Is.LessThan(1e-3f), "Near in: straight at it.");
            Assert.That(math.distance(MissileArcMath.WeaveAim(default, far, target, 0.3f, 0f), target), Is.LessThan(1e-3f), "No weave: the target.");
        }

        [Test]
        public void Phase_DiffersBetweenSalvoShots()
        {
            Assert.That(MissileArcMath.Phase(1, 0), Is.Not.EqualTo(MissileArcMath.Phase(1, 1)).Within(0.1f));
            Assert.That(MissileArcMath.Phase(1, 3), Is.InRange(0f, 2f * math.PI));
        }
    }
}
