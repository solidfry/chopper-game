using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Mathematics;

namespace Chopper.Tests
{
    /// <summary>Harder crash rules (owner, 19 Nov 2026): upside down, rotor strikes, fast ground hits.</summary>
    public sealed class CrashRuleTests
    {
        private static readonly CrashDamageConfig Config = CrashDamageConfig.Default;

        [Test]
        public void UpsideDown_HurtsSoonerAndFasterThanOnItsSide()
        {
            float side = 0f, inverted = 0f, sideTime = 0f, invertedTime = 0f;
            for (var i = 0; i < 60; i++) // 2 s at 30 Hz
            {
                side += CrashDamage.Topple(ref sideTime, true, 1f / 30f, Config, 100f);
                inverted += CrashDamage.Topple(ref invertedTime, true, 1f / 30f, Config, 100f, inverted: true);
            }
            Assert.That(side, Is.EqualTo(10f).Within(0.5f), "side: 0.5 s after a 1.5 s grace at 20 %/s");
            Assert.That(inverted, Is.EqualTo(75f).Within(1f), "upside down: 1.5 s after a 0.5 s grace at 50 %/s");
        }

        [Test]
        public void IsInverted_OnlyPastTheLimit()
        {
            Assert.That(CrashDamage.IsInverted(Config, new float3(0f, -1f, 0f)), Is.True);
            Assert.That(CrashDamage.IsInverted(Config, new float3(1f, 0f, 0f)), Is.False, "on its side");
        }

        [Test]
        public void RotorSide_HitFromAbove_HasAFloor()
        {
            var up = new float3(0f, 1f, 0f);
            Assert.That(CrashDamage.IsRotorSide(new float3(0f, -1f, 0f), up), Is.True, "pushed down = hit on top");
            Assert.That(CrashDamage.IsRotorSide(new float3(0f, 1f, 0f), up), Is.False, "landing");
            Assert.That(CrashDamage.IsRotorSide(new float3(1f, 0f, 0f), up), Is.False, "wall");
            Assert.That(CrashDamage.WithRotorStrike(Config, 3f, true, 100f), Is.EqualTo(20f).Within(1e-4f));
            Assert.That(CrashDamage.WithRotorStrike(Config, 3f, false, 100f), Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void FastGroundHit_Kills()
        {
            Assert.That(CrashDamage.Damage(Config, 32f, landing: true, 100f), Is.EqualTo(100f).Within(1e-3f));
            Assert.That(CrashDamage.Damage(Config, 25f, landing: true, 100f), Is.GreaterThan(55f));
        }
    }
}
