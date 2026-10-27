using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Mathematics;

namespace Chopper.Tests
{
    public sealed class ClusterTests
    {
        [Test]
        public void Direction_StaysInsideTheConeAndIsUnitLength()
        {
            var forward = math.normalize(new float3(1f, -0.5f, 2f));
            var coneCos = math.cos(math.radians(14f));
            for (var i = 0; i < 64; i++)
            {
                var direction = Cluster.Direction(forward, coneCos, seed: 12345u, index: i);
                Assert.That(math.length(direction), Is.EqualTo(1f).Within(1e-4f));
                Assert.That(math.dot(direction, forward), Is.GreaterThanOrEqualTo(coneCos - 1e-4f));
            }
        }

        [Test]
        public void Direction_SameSeedSameSpread_OtherSeedOtherSpread()
        {
            var a = Cluster.Direction(math.forward(), 0.9f, 7u, 3);
            var b = Cluster.Direction(math.forward(), 0.9f, 7u, 3);
            var c = Cluster.Direction(math.forward(), 0.9f, 8u, 3);
            Assert.That(math.all(a == b), Is.True, "client and server rebuild the same direction");
            Assert.That(math.all(a == c), Is.False);
        }

        [Test]
        public void Direction_WorksStraightDown()
        {
            var direction = Cluster.Direction(new float3(0f, -1f, 0f), math.cos(math.radians(10f)), 1u, 0);
            Assert.That(direction.y, Is.LessThan(-0.98f));
        }

        [Test]
        public void ShouldSplit_OnTimerOrNearTarget()
        {
            var config = new ClusterConfig { Count = 8, SplitDistance = 60f };
            Assert.That(Cluster.ShouldSplit(config, splitRemaining: 1f, targetDistance: -1f), Is.False);
            Assert.That(Cluster.ShouldSplit(config, splitRemaining: 0f, targetDistance: -1f), Is.True);
            Assert.That(Cluster.ShouldSplit(config, splitRemaining: 1f, targetDistance: 50f), Is.True);
            Assert.That(Cluster.ShouldSplit(config, splitRemaining: 1f, targetDistance: 100f), Is.False);
            Assert.That(Cluster.ShouldSplit(new ClusterConfig(), splitRemaining: 0f, targetDistance: 0f), Is.False, "not a cluster");
        }

        [Test]
        public void Salvo_FlareResistanceIsSpreadThroughTheSalvo()
        {
            var resistant = new System.Collections.Generic.List<int>();
            for (var shot = 0; shot < 10; shot++)
                if (Salvo.IgnoresFlares(shot, 0.3f)) resistant.Add(shot);
            Assert.That(resistant, Is.EqualTo(new[] { 3, 6, 9 }));

            Assert.That(Salvo.IgnoresFlares(0, 0f), Is.False, "no resistance");
            Assert.That(Salvo.IgnoresFlares(0, 1f), Is.True, "full resistance");
        }
    }
}
