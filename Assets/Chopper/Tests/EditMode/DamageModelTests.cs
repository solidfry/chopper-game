using System.Collections.Generic;
using Chopper.Presentation.Camera;
using Chopper.Presentation.Effects;
using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Tests
{
    public sealed class DamageModelTests
    {
        private static readonly CrashDamageConfig Config = CrashDamageConfig.Default;

        [Test]
        public void ImpactSpeed_IsTheVelocityTheContactTookAway()
        {
            // Flying at 20 m/s into a wall that faces -Z: the wall stops the chopper.
            var speed = CrashDamage.ImpactSpeed(new float3(0f, 0f, 20f), float3.zero, out var normal);
            Assert.That(speed, Is.EqualTo(20f).Within(1e-4f));
            Assert.That(normal.z, Is.EqualTo(-1f).Within(1e-4f));
            Assert.That(CrashDamage.IsLanding(normal), Is.False);

            // Falling at 12 m/s onto the ground: a landing.
            CrashDamage.ImpactSpeed(new float3(5f, -12f, 0f), new float3(5f, 0f, 0f), out normal);
            Assert.That(CrashDamage.IsLanding(normal), Is.True);
        }

        [Test]
        public void Damage_IsZeroUpToTheSafeSpeed_AndAllHealthAtTheLethalSpeed()
        {
            Assert.That(CrashDamage.Damage(Config, Config.SafeImpactSpeed, landing: false, 100f), Is.EqualTo(0f));
            Assert.That(CrashDamage.Damage(Config, Config.LethalImpactSpeed, landing: false, 100f), Is.EqualTo(100f).Within(1e-3f));
            Assert.That(CrashDamage.Damage(Config, 200f, landing: false, 100f), Is.EqualTo(100f).Within(1e-3f));
        }

        [Test]
        public void Damage_LandingHasItsOwnSafeSpeed()
        {
            // 6 m/s: a firm touchdown is safe, the same bump into a wall is not.
            Assert.That(CrashDamage.Damage(Config, 6f, landing: true, 100f), Is.EqualTo(0f));
            Assert.That(CrashDamage.Damage(Config, 6f, landing: false, 100f), Is.GreaterThan(0f));
        }

        [Test]
        public void Damage_CurveKeepsBumpsMild()
        {
            var bump = CrashDamage.Damage(Config, 15f, landing: false, 100f);
            var crash = CrashDamage.Damage(Config, 30f, landing: false, 100f);
            Assert.That(bump, Is.LessThan(20f));
            Assert.That(crash, Is.GreaterThan(bump * 3f), "Twice the speed is much more than twice the damage.");
        }

        [Test]
        public void Smoke_StartsBelowItsShare_AndGrowsToFull()
        {
            Assert.That(DamageLook.Smoke(0.8f, 0.7f), Is.EqualTo(0f));
            Assert.That(DamageLook.Smoke(0.35f, 0.7f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(DamageLook.Smoke(0f, 0.7f), Is.EqualTo(1f));
            Assert.That(DamageLook.Fire(0.3f, 0.2f), Is.EqualTo(0f));
        }

        [Test]
        public void SmokeColour_GoesFromLightToHeavy()
        {
            var light = Color.white;
            var heavy = Color.black;
            Assert.That(DamageLook.SmokeColour(0.7f, 0.7f, 0.4f, light, heavy), Is.EqualTo(light));
            Assert.That(DamageLook.SmokeColour(0.2f, 0.7f, 0.4f, light, heavy), Is.EqualTo(heavy));
        }

        [Test]
        public void Sparks_StayInsideTheRange()
        {
            var range = new Vector2Int(6, 48);
            Assert.That(DamageLook.Sparks(0.5f, DamageKind.Bullet, 1.5f, range), Is.EqualTo(6));
            Assert.That(DamageLook.Sparks(1000f, DamageKind.Explosion, 1.5f, range), Is.EqualTo(48));
            Assert.That(DamageLook.Sparks(10f, DamageKind.Explosion, 1.5f, range),
                Is.GreaterThan(DamageLook.Sparks(10f, DamageKind.Landing, 1.5f, range)));
        }

        [Test]
        public void AddPoint_MergesNearHits_AndKeepsTheNewest()
        {
            var points = new List<(Vector3 Point, Vector3 Normal)>();
            DamageLook.AddPoint(points, new Vector3(0f, 0f, 0f), Vector3.up);
            DamageLook.AddPoint(points, new Vector3(0.3f, 0f, 0f), Vector3.up);
            Assert.That(points.Count, Is.EqualTo(1), "A hit next to a hole moves the hole.");

            DamageLook.AddPoint(points, new Vector3(2f, 0f, 0f), Vector3.up);
            DamageLook.AddPoint(points, new Vector3(4f, 0f, 0f), Vector3.up);
            DamageLook.AddPoint(points, new Vector3(6f, 0f, 0f), Vector3.up);
            Assert.That(points.Count, Is.EqualTo(DamageLook.MaxPoints));
            Assert.That(points[0].Point.x, Is.EqualTo(2f), "The oldest hole went.");
            Assert.That(DamageLook.LeavesMark(DamageKind.Landing), Is.False);
        }
    
        [Test]
        public void Track_CountsOneHitOverSeveralTicksAsOneImpact()
        {
            var sensor = new CrashSensor();
            const float tick = 1f / 60f;
            // Measured 6 Nov: a 21.8 m/s hit on the ground came as 18.4 m/s, then 3.4 m/s.
            Assert.That(CrashDamage.Track(ref sensor, new float3(0f, 18.4f, 0f), tick, out var total, out var added, out var normal), Is.True);
            Assert.That(total, Is.EqualTo(18.4f).Within(1e-3f));
            Assert.That(added, Is.EqualTo(18.4f).Within(1e-3f));
            Assert.That(CrashDamage.IsLanding(normal), Is.True);
            Assert.That(CrashDamage.Track(ref sensor, new float3(0f, 3.4f, 0f), tick, out total, out added, out _), Is.True);
            Assert.That(total, Is.EqualTo(21.8f).Within(1e-3f));
            Assert.That(added, Is.EqualTo(3.4f).Within(1e-3f), "The shake takes only the new part.");
        }

        [Test]
        public void Track_IgnoresGravityAndStartsANewImpactAfterTheWindow()
        {
            var sensor = new CrashSensor();
            const float tick = 1f / 60f;
            Assert.That(CrashDamage.Track(ref sensor, new float3(0f, -0.27f, 0f), tick, out _, out _, out _), Is.False);
            CrashDamage.Track(ref sensor, new float3(10f, 0f, 0f), tick, out _, out _, out _);
            for (var i = 0; i < 10; i++) CrashDamage.Track(ref sensor, float3.zero, tick, out _, out _, out _);
            CrashDamage.Track(ref sensor, new float3(0f, 0f, 6f), tick, out var total, out _, out _);
            Assert.That(total, Is.EqualTo(6f).Within(1e-3f), "A later hit does not add to the old one.");
        }

        [Test]
        public void HitTrauma_MatchesTheImpact()
        {
            var settings = ScriptableObject.CreateInstance<CameraSettings>();
            try
            {
                // Crashes shake by speed: a hard hit shakes hard even when the curve keeps its damage low.
                Assert.That(settings.HitTrauma(DamageKind.Impact, 1f, 30f), Is.EqualTo(1f).Within(1e-4f));
                Assert.That(settings.HitTrauma(DamageKind.Impact, 1f, 15f), Is.EqualTo(0.5f).Within(1e-4f));
                Assert.That(settings.HitTrauma(DamageKind.Landing, 1f, 15f), Is.LessThan(settings.HitTrauma(DamageKind.Impact, 1f, 15f)));
                // Guns and blasts shake by damage; a blast more than a bullet.
                Assert.That(settings.HitTrauma(DamageKind.Bullet, 20f, 0f), Is.EqualTo(0.5f).Within(1e-4f));
                Assert.That(settings.HitTrauma(DamageKind.Explosion, 20f, 0f), Is.GreaterThan(0.5f));
                Assert.That(settings.HitTrauma(DamageKind.Bounds, 50f, 0f), Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
