using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Mathematics;

namespace Chopper.Tests
{
    public sealed class TurretRulesTests
    {
        [Test]
        public void NeutralTurret_ShootsEveryone_TeamTurretSparesItsTeam()
        {
            Assert.That(TurretRules.IsEnemy(0, 0), Is.True);
            Assert.That(TurretRules.IsEnemy(0, 2), Is.True);
            Assert.That(TurretRules.IsEnemy(1, 1), Is.False);
            Assert.That(TurretRules.IsEnemy(1, 2), Is.True);
        }

        [Test]
        public void Slew_TurnsNoMoreThanTheStep_ThenReachesTheTarget()
        {
            var aim = new float3(0, 0, 1);
            var wanted = new float3(1, 0, 0);
            var step = math.radians(30f);
            var next = TurretRules.Slew(aim, wanted, step);
            Assert.That(math.degrees(TurretRules.Angle(aim, next)), Is.EqualTo(30f).Within(0.1f));
            for (var i = 0; i < 3; i++) next = TurretRules.Slew(next, wanted, step);
            Assert.That(TurretRules.Angle(next, wanted), Is.LessThan(1e-3f));
        }

        [Test]
        public void Slew_HandlesATargetStraightBehind()
        {
            var next = TurretRules.Slew(new float3(0, 0, 1), new float3(0, 0, -1), math.radians(10f));
            Assert.That(math.degrees(TurretRules.Angle(new float3(0, 0, 1), next)), Is.EqualTo(10f).Within(0.1f));
        }

        [Test]
        public void CanFire_NeedsCooldownAndTheCone()
        {
            var aim = new float3(0, 0, 1);
            var near = math.normalize(new float3(0.03f, 0, 1));
            var far = math.normalize(new float3(0.5f, 0, 1));
            Assert.That(TurretRules.CanFire(0f, aim, near, math.radians(4f)), Is.True);
            Assert.That(TurretRules.CanFire(0.1f, aim, near, math.radians(4f)), Is.False);
            Assert.That(TurretRules.CanFire(0f, aim, far, math.radians(4f)), Is.False);
        }

        [Test]
        public void Scatter_StaysInsideTheSpread()
        {
            var random = Random.CreateFromIndex(7);
            var aim = new float3(0, 0, 1);
            for (var i = 0; i < 200; i++)
            {
                var shot = TurretRules.Scatter(aim, math.radians(2f), ref random);
                Assert.That(math.degrees(TurretRules.Angle(aim, shot)), Is.LessThanOrEqualTo(2.01f));
            }
        }

        [Test]
        public void Respawn_DownForTheDelay_ThenBack()
        {
            var s = new TurretState();
            Assert.That(TurretRules.TickRespawn(ref s, dead: true, 45f, 0.5f), Is.False);
            Assert.That(s.Down, Is.True);
            var back = false;
            for (var t = 0f; t < 46f && !back; t += 0.5f) back = TurretRules.TickRespawn(ref s, true, 45f, 0.5f);
            Assert.That(back, Is.True);
            Assert.That(s.Down, Is.False);
            Assert.That(TurretRules.TickRespawn(ref s, dead: false, 45f, 0.5f), Is.False, "A live turret does not count down.");
        }

        [Test]
        public void HealthFor_ScalesWithTheBiggerTeam_WithinFloorAndCap()
        {
            var config = new TurretConfig { HealthPerPilot = 60f, MinHealth = 80f, MaxHealth = 240f };
            Assert.That(TurretRules.HealthFor(config, 1), Is.EqualTo(80f));
            Assert.That(TurretRules.HealthFor(config, 2), Is.EqualTo(120f));
            Assert.That(TurretRules.HealthFor(config, 4), Is.EqualTo(240f));
            Assert.That(TurretRules.HealthFor(config, 8), Is.EqualTo(240f));
            Assert.That(TurretRules.HealthFor(new TurretConfig { MaxHealth = 300f }, 2), Is.EqualTo(300f), "No scale: always max.");
        }
    }
}
