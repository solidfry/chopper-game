using Chopper.Simulation.Combat;
using NUnit.Framework;
using Unity.Entities;

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
    }
}
