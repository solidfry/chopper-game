using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Chopper.Tests
{
    public sealed class RepairDroneTests
    {
        private static readonly Entity Self = new() { Index = 7, Version = 1 };

        private static Health Hurt(float current) => new() { Current = current, Max = 100f };

        private static RepairDroneState Launched(float distance = 0f)
        {
            var drone = default(RepairDroneState);
            RepairDrone.Launch(ref drone, Self, distance, speed: 40f, healShare: 0.4f, healSeconds: 4f, targetMax: 100f);
            return drone;
        }

        private static float Run(ref RepairDroneState drone, ref Health health, float seconds, bool valid = true)
        {
            var healed = 0f;
            for (var t = 0f; t < seconds; t += 0.02f) healed += RepairDrone.Step(ref drone, ref health, valid, 0f, 0.02f);
            return healed;
        }

        [Test]
        public void CanLaunch_OnlyDockedAndHurt()
        {
            var docked = default(RepairDroneState);
            Assert.That(RepairDrone.CanLaunch(docked, Hurt(50f)), Is.True);
            Assert.That(RepairDrone.CanLaunch(docked, Hurt(100f)), Is.False, "full hull: never wasted");
            Assert.That(RepairDrone.CanLaunch(docked, Hurt(0f)), Is.False, "dead");
            Assert.That(RepairDrone.CanLaunch(Launched(), Hurt(50f)), Is.False, "already out");
        }

        [Test]
        public void Trip_FliesHealsTheShareAndDocks()
        {
            var drone = Launched();
            var health = Hurt(20f);
            Assert.That(drone.State, Is.EqualTo(DronePhase.Outbound));
            Assert.That(drone.PhaseLength, Is.EqualTo(RepairDrone.MinFlightSeconds), "own chopper: the shortest flight");

            var healed = Run(ref drone, ref health, RepairDrone.MinFlightSeconds + 4.1f);
            Assert.That(healed, Is.EqualTo(40f).Within(1f), "40% of 100 over the heal");
            Assert.That(health.Current, Is.EqualTo(60f).Within(1f));
            Assert.That(drone.State, Is.EqualTo(DronePhase.Returning));

            Run(ref drone, ref health, RepairDrone.MinFlightSeconds + 0.1f);
            Assert.That(drone.State, Is.EqualTo(DronePhase.Docked));
        }

        [Test]
        public void Heal_StopsAtFullAndReturnsEarly()
        {
            var drone = Launched();
            var health = Hurt(90f);
            Run(ref drone, ref health, RepairDrone.MinFlightSeconds + 2f);
            Assert.That(health.Current, Is.EqualTo(100f));
            Assert.That(drone.State, Is.Not.EqualTo(DronePhase.Healing));
        }

        [Test]
        public void LostTarget_SendsItHome()
        {
            var drone = Launched();
            var health = Hurt(50f);
            Run(ref drone, ref health, 0.1f, valid: false);
            Assert.That(drone.State, Is.EqualTo(DronePhase.Returning));
            Assert.That(health.Current, Is.EqualTo(50f));
        }

        [Test]
        public void Flight_TakesLongerToAFarTarget()
        {
            Assert.That(Launched(distance: 200f).PhaseLength, Is.EqualTo(5f).Within(0.01f), "200 m at 40 m/s");
        }

        private static readonly Entity Mate = new() { Index = 8, Version = 1 };
        private static readonly Entity Other = new() { Index = 9, Version = 1 };

        private static Entity Pick(params DroneAlly[] allies)
        {
            using var list = new NativeArray<DroneAlly>(allies, Allocator.Temp);
            return RepairDrone.PickTarget(Self, float3.zero, new float3(0f, 0f, 1f), list, range: 400f, coneCos: 0.966f);
        }

        [Test]
        public void PickTarget_IsSelfWithNoTeamMates()
        {
            Assert.That(Pick(), Is.EqualTo(Self));
        }

        [Test]
        public void PickTarget_IsTheHurtTeamMateUnderTheReticle()
        {
            Assert.That(Pick(new DroneAlly { Entity = Mate, Position = new float3(0f, 0f, 100f), Hurt = true }), Is.EqualTo(Mate));
        }

        [Test]
        public void PickTarget_SkipsFullHullOutOfRangeAndOutsideTheCone()
        {
            Assert.That(Pick(new DroneAlly { Entity = Mate, Position = new float3(0f, 0f, 100f), Hurt = false }), Is.EqualTo(Self), "Full hull.");
            Assert.That(Pick(new DroneAlly { Entity = Mate, Position = new float3(0f, 0f, 500f), Hurt = true }), Is.EqualTo(Self), "Too far.");
            Assert.That(Pick(new DroneAlly { Entity = Mate, Position = new float3(100f, 0f, 100f), Hurt = true }), Is.EqualTo(Self), "45 degrees off.");
        }

        [Test]
        public void PickTarget_PrefersTheMateNearestTheReticle()
        {
            Assert.That(Pick(
                new DroneAlly { Entity = Other, Position = new float3(10f, 0f, 100f), Hurt = true },
                new DroneAlly { Entity = Mate, Position = new float3(0f, 2f, 200f), Hurt = true }), Is.EqualTo(Mate));
        }
    }
}
