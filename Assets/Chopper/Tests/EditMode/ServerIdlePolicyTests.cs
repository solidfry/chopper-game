using Chopper.Simulation.Session;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class ServerIdlePolicyTests
    {
        [Test]
        public void ShouldStop_WhenNobodyJoinsInTime()
        {
            var policy = new ServerIdlePolicy(300f, 120f, 0.0);
            Assert.That(policy.ShouldStop(0, 299.0), Is.False);
            Assert.That(policy.ShouldStop(0, 300.0), Is.True);
        }

        [Test]
        public void ShouldStop_CountsFromTheLastPlayerLeaving()
        {
            var policy = new ServerIdlePolicy(300f, 120f, 0.0);
            Assert.That(policy.ShouldStop(2, 50.0), Is.False);
            Assert.That(policy.ShouldStop(2, 1000.0), Is.False, "Never stops with players on.");
            Assert.That(policy.ShouldStop(0, 1001.0), Is.False);
            Assert.That(policy.ShouldStop(0, 1120.0), Is.False);
            Assert.That(policy.ShouldStop(0, 1121.0), Is.True);
        }

        [Test]
        public void ShouldStop_RejoinResetsTheEmptyTimer()
        {
            var policy = new ServerIdlePolicy(300f, 120f, 0.0);
            policy.ShouldStop(1, 10.0);
            policy.ShouldStop(0, 20.0);
            policy.ShouldStop(1, 130.0);
            Assert.That(policy.ShouldStop(0, 140.0), Is.False);
            Assert.That(policy.ShouldStop(0, 259.0), Is.False);
            Assert.That(policy.ShouldStop(0, 260.0), Is.True);
        }

        [Test]
        public void ShouldStop_ZeroTimeoutMeansNever()
        {
            var policy = new ServerIdlePolicy(0f, 0f, 0.0);
            Assert.That(policy.ShouldStop(0, 1e6), Is.False);
            policy.ShouldStop(1, 1e6);
            Assert.That(policy.ShouldStop(0, 2e6), Is.False);
        }
    }
}
