using System.Collections.Generic;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Definitions;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Tests
{
    /// <summary>The rocket path, proximity fuse, rocket salvo and gun falloff maths behind the larger weapon roster.</summary>
    public sealed class WeaponRosterTests
    {
        private const float Tick = 1f / 60f;

        [Test]
        public void Step_WithNoGravityOrDrag_IsAStraightLine()
        {
            var velocity = new float3(0f, 0f, 120f);
            var position = float3.zero;
            for (var i = 0; i < 60; i++) position = RocketFlight.Step(position, ref velocity, 0f, 0f, Tick);

            Assert.That(position.z, Is.EqualTo(120f).Within(0.01f));
            Assert.That(position.y, Is.EqualTo(0f));
            Assert.That(velocity.z, Is.EqualTo(120f));
        }

        [Test]
        public void Step_WithGravity_FallsLikeABomb()
        {
            var velocity = new float3(0f, 0f, 30f);
            var position = float3.zero;
            for (var i = 0; i < 120; i++) position = RocketFlight.Step(position, ref velocity, 25f, 0f, Tick);

            // 2 s: half g t squared = 50 m (the tick step lands a little lower).
            Assert.That(position.y, Is.EqualTo(-50f).Within(1f));
            Assert.That(position.z, Is.EqualTo(60f).Within(0.01f));
            Assert.That(velocity.y, Is.EqualTo(-50f).Within(0.01f));
        }

        [Test]
        public void Step_WithDrag_StopsLikeAMine()
        {
            var velocity = new float3(0f, 0f, 60f);
            var position = float3.zero;
            for (var i = 0; i < 300; i++) position = RocketFlight.Step(position, ref velocity, 0f, 2f, Tick);

            Assert.That(math.length(velocity), Is.LessThan(0.01f));
            // Total path = speed / drag.
            Assert.That(position.z, Is.EqualTo(30f).Within(0.6f));
        }

        [Test]
        public void IsBallistic_OnlyWithGravityOrDrag()
        {
            Assert.That(RocketFlight.IsBallistic(0f, 0f), Is.False);
            Assert.That(RocketFlight.IsBallistic(4f, 0f), Is.True);
            Assert.That(RocketFlight.IsBallistic(0f, 2f), Is.True);
        }

        [Test]
        public void FuseHit_WhenThePathPassesInsideTheRadius()
        {
            // The step flies past the target, 4 m to its side: closer than either end of the step.
            var hit = RocketFlight.FuseHit(new float3(0f, 0f, -10f), new float3(0f, 0f, 10f), new float3(4f, 0f, 0f), 6f, out var point);

            Assert.That(hit, Is.True);
            Assert.That(math.distance(point, float3.zero), Is.LessThan(1e-4f));
        }

        [Test]
        public void FuseHit_NotWhenTheTargetIsTooFar()
        {
            Assert.That(RocketFlight.FuseHit(float3.zero, new float3(0f, 0f, 3f), new float3(8f, 0f, 1f), 6f, out _), Is.False);
            // Ahead of the step, outside the radius: not yet.
            Assert.That(RocketFlight.FuseHit(float3.zero, new float3(0f, 0f, 3f), new float3(0f, 0f, 20f), 6f, out _), Is.False);
        }

        [Test]
        public void FuseHit_NeverWithoutAFuse()
        {
            Assert.That(RocketFlight.FuseHit(float3.zero, new float3(0f, 0f, 3f), new float3(0f, 0f, 1f), 0f, out _), Is.False);
        }

        [Test]
        public void FuseHit_WorksForAMineThatDoesNotMove()
        {
            var mine = new float3(5f, 50f, 5f);
            Assert.That(RocketFlight.FuseHit(mine, mine, mine + new float3(0f, 0f, 9f), 10f, out var point), Is.True);
            Assert.That(math.distance(point, mine), Is.LessThan(1e-4f));
        }

        [Test]
        public void SalvoDirection_ASingleRocketFliesStraight()
        {
            var forward = math.normalize(new float3(0.2f, 0.1f, 1f));
            Assert.That(RocketFlight.SalvoDirection(forward, 1, 0.9f, 3, 0), Is.EqualTo(forward));
            // A salvo with no cone also flies straight.
            Assert.That(RocketFlight.SalvoDirection(forward, 6, 1f, 3, 4), Is.EqualTo(forward));
        }

        [Test]
        public void SalvoDirection_FansInsideTheCone_SameOnEveryMachine()
        {
            var forward = math.forward();
            var coneCos = math.cos(math.radians(3f));
            var different = false;
            for (var shot = 0; shot < 6; shot++)
            {
                var a = RocketFlight.SalvoDirection(forward, 6, coneCos, 5, shot);
                var b = RocketFlight.SalvoDirection(forward, 6, coneCos, 5, shot);
                Assert.That(a, Is.EqualTo(b));
                Assert.That(math.dot(a, forward), Is.GreaterThanOrEqualTo(coneCos - 1e-4f));
                different |= math.distance(a, RocketFlight.SalvoDirection(forward, 6, coneCos, 5, 0)) > 1e-4f;
            }
            Assert.That(different, Is.True);
        }

        [Test]
        public void GunDamage_WithoutFalloff_IsFullToTheRange()
        {
            var gun = new HitscanWeaponConfig { Damage = 10f, Range = 600f };
            Assert.That(GunDamage.At(gun, 0f), Is.EqualTo(10f));
            Assert.That(GunDamage.At(gun, 600f), Is.EqualTo(10f));
        }

        [Test]
        public void GunDamage_WithFalloff_DropsToTheFloorAtFullRange()
        {
            var gun = new HitscanWeaponConfig { Damage = 10f, Range = 400f, FalloffStart = 200f, FalloffFloor = 0.5f };
            Assert.That(GunDamage.At(gun, 100f), Is.EqualTo(10f));
            Assert.That(GunDamage.At(gun, 200f), Is.EqualTo(10f));
            Assert.That(GunDamage.At(gun, 300f), Is.EqualTo(7.5f).Within(1e-4f));
            Assert.That(GunDamage.At(gun, 400f), Is.EqualTo(5f).Within(1e-4f));
            // Past the range (the ray stops there): never below the floor.
            Assert.That(GunDamage.At(gun, 900f), Is.EqualTo(5f).Within(1e-4f));
        }

        [Test]
        public void GunDamage_AFalloffStartAtOrPastTheRange_IsOff()
        {
            var gun = new HitscanWeaponConfig { Damage = 10f, Range = 400f, FalloffStart = 400f, FalloffFloor = 0f };
            Assert.That(GunDamage.At(gun, 400f), Is.EqualTo(10f));
        }

        [Test]
        public void RocketDefinition_ADefaultRocket_IsStraightSingleAndContact()
        {
            var rocket = ScriptableObject.CreateInstance<RocketWeaponDefinition>();
            try
            {
                Assert.That(rocket.Gravity, Is.EqualTo(0f));
                Assert.That(rocket.Drag, Is.EqualTo(0f));
                Assert.That(rocket.ProximityFuse, Is.EqualTo(0f));
                Assert.That(rocket.FuseTargets, Is.EqualTo(TargetClass.None));
                Assert.That(rocket.SalvoCount, Is.EqualTo(1));
                Assert.That(rocket.SalvoConeCos, Is.EqualTo(1f));
                var problems = new List<string>();
                rocket.CollectProblems(problems);
                Assert.That(problems, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(rocket);
            }
        }

        [Test]
        public void RocketDefinition_ReportsASalvoLongerThanTheFireInterval()
        {
            var rocket = ScriptableObject.CreateInstance<RocketWeaponDefinition>();
            try
            {
                rocket.Initialise(interval: 0.3f, rocketSpeed: 150f, life: 4f, direct: 10f, splash: 10f, radius: 5f, magazine: 3, reload: 12f);
                rocket.SetSalvo(count: 6, interval: 0.07f, spreadDegrees: 3f);
                var problems = new List<string>();
                rocket.CollectProblems(problems);
                Assert.That(problems, Has.Count.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(rocket);
            }
        }

        [Test]
        public void RocketDefinition_ReportsAFuseWiderThanTheSplash()
        {
            var rocket = ScriptableObject.CreateInstance<RocketWeaponDefinition>();
            try
            {
                rocket.Initialise(interval: 0.8f, rocketSpeed: 170f, life: 3.5f, direct: 20f, splash: 28f, radius: 5f, magazine: 8, reload: 9f);
                rocket.SetFuse(radius: 6f, TargetClass.Air);
                var problems = new List<string>();
                rocket.CollectProblems(problems);
                Assert.That(problems, Has.Count.EqualTo(1));
                Assert.That(rocket.FuseTargets, Is.EqualTo(TargetClass.Air));
            }
            finally
            {
                Object.DestroyImmediate(rocket);
            }
        }

        [Test]
        public void RocketScale_FollowsTheBlast_InsideLimits()
        {
            Assert.That(Chopper.Presentation.Effects.RocketVisualSystem.RocketScale(8f), Is.EqualTo(1f));
            Assert.That(Chopper.Presentation.Effects.RocketVisualSystem.RocketScale(4f), Is.EqualTo(0.6f));
            Assert.That(Chopper.Presentation.Effects.RocketVisualSystem.RocketScale(14f), Is.EqualTo(1.75f));
            Assert.That(Chopper.Presentation.Effects.RocketVisualSystem.RocketScale(40f), Is.EqualTo(2.2f));
            // Not given (an old message): standard size.
            Assert.That(Chopper.Presentation.Effects.RocketVisualSystem.RocketScale(0f), Is.EqualTo(1f));
        }

        [Test]
        public void ModuleSummary_ShowsTheNumbersOfAGunARocketAndOptics()
        {
            var gun = ScriptableObject.CreateInstance<HitscanWeaponDefinition>();
            var rocket = ScriptableObject.CreateInstance<RocketWeaponDefinition>();
            var optics = ScriptableObject.CreateInstance<OpticsDefinition>();
            try
            {
                gun.Initialise(rpm: 600f, damage: 10f, maxRange: 500f);
                gun.SetMaxZoom(4f);
                Assert.That(Chopper.Presentation.Hangar.ModuleSummary.Gun(gun), Is.EqualTo("100 DPS  500 M  ZOOM TO 4x"));

                rocket.Initialise(interval: 3f, rocketSpeed: 150f, life: 4f, direct: 14f, splash: 10f, radius: 5f, magazine: 3, reload: 12f);
                rocket.SetSalvo(count: 6, interval: 0.07f, spreadDegrees: 3f);
                Assert.That(Chopper.Presentation.Hangar.ModuleSummary.Rocket(rocket), Is.EqualTo("24 DMG  5 M BLAST  3 x 6  150 M/S"));
                rocket.SetPath(gravityAcceleration: 25f, dragRate: 0f);
                Assert.That(Chopper.Presentation.Hangar.ModuleSummary.Rocket(rocket), Does.Contain("FALLS"));

                optics.Initialise(2f, 4f);
                optics.SetVision(VisionMode.Thermal, 1200f);
                Assert.That(Chopper.Presentation.Hangar.ModuleSummary.Optics(optics), Is.EqualTo("ZOOM 2x / 4x  THERMAL 1200 M"));
                Assert.That(Chopper.Presentation.Hangar.ModuleSummary.Text(null), Is.EqualTo(""));
            }
            finally
            {
                Object.DestroyImmediate(gun);
                Object.DestroyImmediate(rocket);
                Object.DestroyImmediate(optics);
            }
        }

        [Test]
        public void GunDefinition_ReportsAFalloffStartPastTheRange()
        {
            var gun = ScriptableObject.CreateInstance<HitscanWeaponDefinition>();
            try
            {
                gun.Initialise(rpm: 1500f, damage: 4.5f, maxRange: 450f);
                gun.SetFalloff(start: 500f, floor: 0.5f);
                var problems = new List<string>();
                gun.CollectProblems(problems);
                Assert.That(problems, Has.Count.EqualTo(1));
                Assert.That(gun.DamagePerSecond, Is.EqualTo(112.5f).Within(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(gun);
            }
        }
    }
}
