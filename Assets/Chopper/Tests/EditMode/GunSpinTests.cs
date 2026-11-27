using Chopper.Simulation.Combat;
using NUnit.Framework;

namespace Chopper.Tests.EditMode
{
    /// <summary>Minigun spin-up (owner 27 Nov): no shot until the barrels have spun for the spin-up time.</summary>
    public sealed class GunSpinTests
    {
        private const float Up = 1f / 0.5f;
        private const float Down = 1f / 0.8f;
        private const float Tick = 1f / 60f;

        [Test]
        public void GunWithNoSpinUp_IsAlwaysReady()
        {
            Assert.That(GunSpin.Ready(GunSpin.Step(0f, false, 0f, Down, Tick)), Is.True);
        }

        [Test]
        public void HeldTrigger_ReadyAfterHalfASecondNotBefore()
        {
            var spin = 0f;
            var ticks = 0;
            while (!GunSpin.Ready(spin) && ticks < 1000)
            {
                spin = GunSpin.Step(spin, true, Up, Down, Tick);
                ticks++;
            }
            Assert.That(ticks * Tick, Is.EqualTo(0.5f).Within(Tick * 1.5f));
        }

        [Test]
        public void ReleasedTrigger_SpinsDownAndAQuickRefireWaitsLess()
        {
            var spin = 1f;
            for (var i = 0; i < 12; i++) spin = GunSpin.Step(spin, false, Up, Down, Tick); // 0.2 s off
            Assert.That(spin, Is.LessThan(1f).And.GreaterThan(0f));
            var ticks = 0;
            while (!GunSpin.Ready(spin)) { spin = GunSpin.Step(spin, true, Up, Down, Tick); ticks++; }
            Assert.That(ticks * Tick, Is.LessThan(0.5f));
        }
    }
}
