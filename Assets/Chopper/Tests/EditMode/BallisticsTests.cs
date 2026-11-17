using Chopper.Simulation.Bots;
using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Mathematics;

namespace Chopper.Tests
{
    /// <summary>
    /// Gun rounds with travel time (<see cref="Ballistics"/>, Docs/GunBulletTravelDesign.md): the per-tick step, expiry at
    /// range, instant guns, and the shared lead solution used by the HUD reticle and the bots (gun and rockets).
    /// No physics world: a target is a point with a radius, and a tick "hits" when the round passes that close.
    /// </summary>
    public sealed class BallisticsTests
    {
        private const float Tick = 1f / 60f;
        private const float TargetRadius = 2f;

        private static ActiveBullet Fire(float3 muzzle, float3 shooterVelocity, float3 forward, float speed, float range) => new()
        {
            Position = muzzle,
            Velocity = Ballistics.LaunchVelocity(shooterVelocity, forward, speed),
            TimeLeft = Ballistics.FlightTime(range, speed),
        };

        /// <summary>Closest distance in one tick between a round going a→b and a target going c→d (both straight in the tick).</summary>
        private static float ClosestApproach(float3 a, float3 b, float3 c, float3 d)
        {
            var start = a - c;
            var step = (b - d) - start;
            var lengthSq = math.lengthsq(step);
            var t = lengthSq > 1e-8f ? math.saturate(-math.dot(start, step) / lengthSq) : 0f;
            return math.length(start + step * t);
        }

        /// <summary>Flies the round until it expires; true when it passes within the radius of the moving target.</summary>
        private static bool Hits(ActiveBullet bullet, float3 target, float3 targetVelocity, out float closest)
        {
            closest = float.MaxValue;
            for (var i = 0; i < 10000; i++)
            {
                if (!Ballistics.Step(ref bullet, Tick, out var from)) return false;
                var targetNext = target + targetVelocity * Tick;
                closest = math.min(closest, ClosestApproach(from, bullet.Position, target, targetNext));
                if (closest <= TargetRadius) return true;
                target = targetNext;
                if (Ballistics.Expired(bullet)) return false;
            }
            Assert.Fail("the round never expired");
            return false;
        }

        [Test]
        public void Step_MovesByVelocityTimesTick_AndCountsTheDistance()
        {
            var bullet = Fire(float3.zero, new float3(0f, 0f, 50f), math.forward(), 850f, 650f);

            Assert.That(Ballistics.Step(ref bullet, Tick, out var from), Is.True);

            Assert.That(from, Is.EqualTo(float3.zero));
            Assert.That(bullet.Position.z, Is.EqualTo(900f * Tick).Within(1e-4f));
            Assert.That(bullet.Travelled, Is.EqualTo(900f * Tick).Within(1e-4f));
            Assert.That(bullet.TimeLeft, Is.EqualTo(650f / 850f - Tick).Within(1e-5f));
        }

        [Test]
        public void Round_ExpiresAtItsRange_AndNeverGoesFurther()
        {
            var bullet = Fire(float3.zero, float3.zero, math.forward(), 900f, 600f);
            var steps = 0;
            while (Ballistics.Step(ref bullet, Tick, out _))
            {
                steps++;
                Assert.That(bullet.Position.z, Is.LessThanOrEqualTo(600f + 1e-2f));
                if (Ballistics.Expired(bullet)) break;
            }

            Assert.That(Ballistics.Expired(bullet), Is.True);
            Assert.That(bullet.Position.z, Is.EqualTo(600f).Within(1e-2f), "the last step stops at the range");
            // 0.667 s at 60 Hz: 40 ticks, maybe one more tiny one from float rounding.
            Assert.That(steps, Is.InRange(40, 41));
            // An expired round does not move again.
            Assert.That(Ballistics.Step(ref bullet, Tick, out _), Is.False);
        }

        [Test]
        public void Round_HitsATargetThatFliesIntoItsPath()
        {
            // The round flies straight ahead; the target crosses from the side and reaches the line when the round does.
            const float speed = 800f, range = 750f, distance = 400f, crossing = 80f;
            var flightTime = distance / speed;
            var target = new float3(-crossing * flightTime, 0f, distance);

            var hit = Hits(Fire(float3.zero, float3.zero, math.forward(), speed, range), target, new float3(crossing, 0f, 0f), out var closest);

            Assert.That(hit, Is.True, $"closest {closest:0.0} m");
        }

        [Test]
        public void Round_MissesATargetThatLeftThePath()
        {
            // The target is on the line when the gun fires but flies off it: with travel time the round arrives too late.
            var hit = Hits(Fire(float3.zero, float3.zero, math.forward(), 800f, 750f), new float3(0f, 0f, 400f), new float3(80f, 0f, 0f),
                out var closest);

            Assert.That(hit, Is.False);
            Assert.That(closest, Is.GreaterThan(30f));
        }

        [Test]
        public void TravelledAt_IsTheDistanceFlownToTheHitPoint()
        {
            var bullet = Fire(float3.zero, float3.zero, math.forward(), 600f, 600f);
            Ballistics.Step(ref bullet, Tick, out _);
            Ballistics.Step(ref bullet, Tick, out _);

            Assert.That(Ballistics.TravelledAt(bullet, new float3(0f, 0f, 15f)), Is.EqualTo(15f).Within(1e-3f));
        }

        [Test]
        public void SpeedZero_StaysInstant()
        {
            Assert.That(Ballistics.IsInstant(0f), Is.True);
            Assert.That(Ballistics.IsInstant(900f), Is.False);
            Assert.That(Ballistics.FlightTime(500f, 0f), Is.EqualTo(0f));
            // An instant gun has no lead: aim at the target.
            Assert.That(Ballistics.LeadPoint(float3.zero, float3.zero, new float3(0f, 0f, 300f), new float3(80f, 0f, 0f), 0f, 1f, out _, out _),
                Is.False);
        }

        [Test]
        public void GunLead_OnACrossingTarget_Hits_WhileAimingAtTheTargetMisses()
        {
            // Both choppers move: the round keeps the shooter's velocity, so the lead works relative to the shooter.
            var muzzle = new float3(0f, 50f, 0f);
            var shooterVelocity = new float3(0f, 0f, 30f);
            var target = new float3(-40f, 60f, 600f);
            var targetVelocity = new float3(80f, 0f, -10f);
            const float speed = 750f, range = 700f;

            Assert.That(Ballistics.LeadPoint(muzzle, shooterVelocity, target, targetVelocity, speed, Ballistics.FlightTime(range, speed),
                out var lead, out var time), Is.True);
            Assert.That(time, Is.GreaterThan(0.5f).And.LessThan(1f));

            var led = Fire(muzzle, shooterVelocity, math.normalize(lead - muzzle), speed, range);
            Assert.That(Hits(led, target, targetVelocity, out var closest), Is.True, $"lead missed by {closest:0.0} m");

            var straight = Fire(muzzle, shooterVelocity, math.normalize(target - muzzle), speed, range);
            Assert.That(Hits(straight, target, targetVelocity, out _), Is.False, "no lead should miss a crossing target");
        }

        [Test]
        public void GunLead_PastTheRange_HasNoPoint()
        {
            Assert.That(Ballistics.LeadPoint(float3.zero, float3.zero, new float3(0f, 0f, 900f), float3.zero, 900f,
                Ballistics.FlightTime(600f, 900f), out _, out _), Is.False);
        }

        [Test]
        public void RocketLead_WithGravityAndOwnVelocity_HitsACrossingTarget()
        {
            // Rocket Pod numbers: rockets keep the chopper's velocity and fall at up to 10 m/s².
            var muzzle = new float3(0f, 80f, 0f);
            var shooterVelocity = new float3(0f, 0f, 40f);
            var target = new float3(-30f, 70f, 300f);
            var targetVelocity = new float3(60f, 0f, 0f);
            const float speed = 210f, gravity = 10f, lifetime = 2.7f;

            Assert.That(Ballistics.LeadPoint(muzzle, shooterVelocity, target, targetVelocity, speed, gravity, lifetime, out var lead, out _),
                Is.True);

            var position = muzzle;
            var velocity = Ballistics.LaunchVelocity(shooterVelocity, math.normalize(lead - muzzle), speed);
            var closest = float.MaxValue;
            for (var time = 0f; time < lifetime; time += Tick)
            {
                var next = RocketFlight.Step(position, ref velocity, gravity, 0f, Tick);
                var targetNext = target + targetVelocity * Tick;
                closest = math.min(closest, ClosestApproach(position, next, target, targetNext));
                position = next;
                target = targetNext;
            }

            Assert.That(closest, Is.LessThan(TargetRadius), $"rocket lead missed by {closest:0.0} m");
        }

        [Test]
        public void BotAimPoint_LeadsTheGun_AndAimsStraightWithAnInstantGun()
        {
            var s = new BotSituation
            {
                Position = float3.zero, Velocity = float3.zero, HasGun = true, GunRange = 700f, GunBulletSpeed = 750f, RocketPylon = -1,
            };
            var target = new float3(0f, 0f, 400f);
            var targetVelocity = new float3(80f, 0f, 0f);

            var aim = BotBrain.AimPoint(s, target, targetVelocity, rockets: false);
            Assert.That(aim.x, Is.GreaterThan(30f), "the gun aim leads the crossing target");

            s.GunBulletSpeed = 0f;
            Assert.That(BotBrain.AimPoint(s, target, targetVelocity, rockets: false), Is.EqualTo(target));
        }

        [Test]
        public void BotAimPoint_Rockets_AimAboveForTheDrop()
        {
            var s = new BotSituation { RocketSpeed = 210f, RocketGravity = 10f, RocketLifetime = 2.7f, RocketPylon = 0 };
            var target = new float3(0f, 0f, 300f);

            var aim = BotBrain.AimPoint(s, target, float3.zero, rockets: true);

            var time = 300f / 210f;
            Assert.That(aim.y, Is.EqualTo(0.5f * 10f * time * time).Within(0.05f));
        }
    }
}
