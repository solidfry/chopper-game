using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Mathematics;

namespace Chopper.Tests
{
    public sealed class GuidanceTests
    {
        [Test]
        public void InCone_AcceptsTargetsAheadAndRejectsOffAxisOrFar()
        {
            var cone = math.cos(math.radians(15f));
            Assert.That(Seeker.InCone(float3.zero, math.forward(), new float3(0, 0, 100), cone, 600f, out var distance, out var alignment), Is.True);
            Assert.That(distance, Is.EqualTo(100f).Within(1e-3f));
            Assert.That(alignment, Is.EqualTo(1f).Within(1e-5f));

            Assert.That(Seeker.InCone(float3.zero, math.forward(), new float3(100, 0, 100), cone, 600f, out _, out _), Is.False, "45° off axis");
            Assert.That(Seeker.InCone(float3.zero, math.forward(), new float3(0, 0, 700), cone, 600f, out _, out _), Is.False, "Out of range");
        }

        [Test]
        public void Score_PrefersHotterCentredCloserTargets()
        {
            Assert.That(Seeker.Score(2f, 1f, 100f), Is.GreaterThan(Seeker.Score(1f, 1f, 100f)), "hotter");
            Assert.That(Seeker.Score(1f, 1f, 100f), Is.GreaterThan(Seeker.Score(1f, 0.97f, 100f)), "centred");
            Assert.That(Seeker.Score(1f, 1f, 100f), Is.GreaterThan(Seeker.Score(1f, 1f, 300f)), "closer");
        }

        [Test]
        public void Heat_RisesWithSpeedAndDash()
        {
            var target = new Targetable { Class = TargetClass.Air, BaseHeat = 1f };
            Assert.That(Seeker.Heat(target, speed: 0f, dashing: false), Is.EqualTo(1f));
            Assert.That(Seeker.Heat(target, speed: 50f, dashing: false), Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(Seeker.Heat(target, speed: 50f, dashing: true), Is.EqualTo(1.5f + Seeker.DashHeat).Within(1e-5f));
        }

        [Test]
        public void Steer_KeepsSpeedAndTurnsNoMoreThanTheTurnRate()
        {
            var velocity = new float3(0, 0, 100);
            var turnRate = math.radians(90f);
            var steered = Guidance.Steer(float3.zero, velocity, new float3(1000, 0, 0), float3.zero, speed: 100f,
                turnRate: turnRate, navigationGain: 3f, deltaTime: 0.1f);

            Assert.That(math.length(steered), Is.EqualTo(100f).Within(1e-3f));
            var turned = math.acos(math.dot(math.normalize(steered), math.forward()));
            Assert.That(turned, Is.EqualTo(turnRate * 0.1f).Within(1e-3f), "Target is 90° off: turn the full step, no more.");
            Assert.That(steered.x, Is.GreaterThan(0f), "Turns towards the target.");
        }

        [Test]
        public void Steer_AimsStraightAtATargetInsideOneStep()
        {
            var steered = Guidance.Steer(float3.zero, new float3(0, 0, 100), new float3(1, 0, 100), float3.zero, 100f,
                math.radians(90f), 0f, 0.1f);
            Assert.That(math.normalize(steered).x, Is.EqualTo(math.normalize(new float3(1, 0, 100)).x).Within(1e-4f));
        }

        [Test]
        public void Steer_LeadsAMovingTarget()
        {
            // Target ahead, moving right: with lead the missile aims right of the target; with no lead, straight at it.
            var withLead = Guidance.Steer(float3.zero, new float3(0, 0, 100), new float3(0, 0, 500), new float3(50, 0, 0), 100f,
                math.radians(360f), navigationGain: 3f, deltaTime: 1f);
            var pursuit = Guidance.Steer(float3.zero, new float3(0, 0, 100), new float3(0, 0, 500), new float3(50, 0, 0), 100f,
                math.radians(360f), navigationGain: 0f, deltaTime: 1f);
            Assert.That(withLead.x, Is.GreaterThan(1f));
            Assert.That(pursuit.x, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void InterceptTime_StationaryTargetIsDistanceOverSpeed()
        {
            Assert.That(Guidance.InterceptTime(new float3(0, 0, 100), float3.zero, 50f, out var time), Is.True);
            Assert.That(time, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void InterceptTime_CrossingTargetMeetsTheProjectile()
        {
            var position = new float3(0, 0, 200);
            var velocity = new float3(30, 0, 0);
            Assert.That(Guidance.InterceptTime(position, velocity, 100f, out var time), Is.True);
            // At that time the target is exactly projectile speed × time away.
            Assert.That(math.length(position + velocity * time), Is.EqualTo(100f * time).Within(1e-2f));
        }

        [Test]
        public void InterceptTime_FalseWhenTheTargetRunsAwayFaster()
        {
            Assert.That(Guidance.InterceptTime(new float3(0, 0, 100), new float3(0, 0, 80), 50f, out _), Is.False);
        }
    }
}
