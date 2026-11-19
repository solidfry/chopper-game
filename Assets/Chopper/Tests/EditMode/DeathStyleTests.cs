using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Mathematics;

namespace Chopper.Tests
{
    public sealed class DeathStyleTests
    {
        private static readonly WreckConfig Config = WreckConfig.Default;
        private const float High = 120f;

        private static DeathBlow Blow(DamageKind kind, float overkill = 1.2f, bool hasPoint = true) =>
            new() { Set = true, Kind = kind, Overkill = overkill, HasPoint = hasPoint };

        [Test]
        public void Pick_CrashesAndAirBurstsAndLowKillsExplodeAtOnce()
        {
            Assert.That(DeathStyles.Pick(Config, Blow(DamageKind.Impact), float3.zero, High), Is.EqualTo(DeathStyle.Explode));
            Assert.That(DeathStyles.Pick(Config, Blow(DamageKind.Toppled, hasPoint: false), float3.zero, High), Is.EqualTo(DeathStyle.Explode));
            Assert.That(DeathStyles.Pick(Config, Blow(DamageKind.Explosion, overkill: 4f), float3.zero, High), Is.EqualTo(DeathStyle.Explode), "air burst");
            Assert.That(DeathStyles.Pick(Config, Blow(DamageKind.Bullet), float3.zero, 3f), Is.EqualTo(DeathStyle.Explode), "too low to fall");
        }

        [Test]
        public void Pick_TheHitZoneSetsHowItFalls()
        {
            var bullet = Blow(DamageKind.Bullet);
            Assert.That(DeathStyles.Pick(Config, bullet, new float3(0f, 0f, -0.8f), High), Is.EqualTo(DeathStyle.TailSpin));
            Assert.That(DeathStyles.Pick(Config, bullet, new float3(0f, 0.8f, 0f), High), Is.EqualTo(DeathStyle.RotorDrop));
            Assert.That(DeathStyles.Pick(Config, bullet, new float3(0f, 0f, 0.8f), High), Is.EqualTo(DeathStyle.NoseDive));
            Assert.That(DeathStyles.Pick(Config, bullet, new float3(0.9f, 0f, 0f), High), Is.EqualTo(DeathStyle.EngineSpiral));
            Assert.That(DeathStyles.Pick(Config, Blow(DamageKind.Burn, hasPoint: false), float3.zero, High), Is.EqualTo(DeathStyle.EngineSpiral));
        }

        [Test]
        public void Motion_TailSpinBuildsUp_RotorDropHasNoLift()
        {
            DeathStyles.Motion(DeathStyle.TailSpin, 0f, 1f, 0f, out var early, out var tailLift);
            DeathStyles.Motion(DeathStyle.TailSpin, 3f, 1f, 0f, out var late, out _);
            Assert.That(math.abs(late.y), Is.GreaterThan(math.abs(early.y)));
            Assert.That(tailLift, Is.GreaterThan(0f));

            DeathStyles.Motion(DeathStyle.RotorDrop, 1f, 1f, 0f, out _, out var dropLift);
            Assert.That(dropLift, Is.EqualTo(0f));

            DeathStyles.Motion(DeathStyle.NoseDive, 1f, 1f, 0.9f, out var held, out _);
            Assert.That(held.x, Is.EqualTo(0f), "nose already well down: stop pitching");
            Assert.That(DeathStyles.SpinSign(new float3(-0.5f, 0f, 0f)), Is.EqualTo(-1));
        }
    }
}
