using Chopper.Simulation.Combat;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class HealthTests
    {
        [Test]
        public void TakeDamage_ReturnsTrueOnlyForTheKillingHit()
        {
            var health = new Health { Current = 10f, Max = 10f };

            Assert.That(health.TakeDamage(6f), Is.False);
            Assert.That(health.Current, Is.EqualTo(4f));

            Assert.That(health.TakeDamage(6f), Is.True);
            Assert.That(health.Current, Is.EqualTo(0f));
            Assert.That(health.IsDead, Is.True);

            Assert.That(health.TakeDamage(6f), Is.False, "A dead chopper cannot be killed again.");
        }

        [Test]
        public void TakeDamage_IgnoresZeroAndNegativeDamage()
        {
            var health = new Health { Current = 10f, Max = 10f };
            Assert.That(health.TakeDamage(0f), Is.False);
            Assert.That(health.TakeDamage(-5f), Is.False);
            Assert.That(health.Current, Is.EqualTo(10f));
        }
    }
}
