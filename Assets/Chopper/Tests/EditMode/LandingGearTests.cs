using Chopper.Simulation.Visuals;
using NUnit.Framework;
using Unity.Mathematics;

namespace Chopper.Tests.EditMode
{
    public sealed class LandingGearTests
    {
        [Test]
        public void WantsUp_HasAGapSoItDoesNotFlapAtTheLine()
        {
            Assert.IsTrue(LandingGear.WantsUp(false, 9f, 8f, 5f), "Above the up height: up.");
            Assert.IsFalse(LandingGear.WantsUp(true, 4f, 8f, 5f), "Below the down height: down.");
            Assert.IsTrue(LandingGear.WantsUp(true, 6.5f, 8f, 5f), "In the gap it keeps going up.");
            Assert.IsFalse(LandingGear.WantsUp(false, 6.5f, 8f, 5f), "In the gap it stays down.");
        }

        [Test]
        public void Step_MovesAtOneOverSecondsAndStopsAtTheEnds()
        {
            Assert.AreEqual(0.5f, LandingGear.Step(1f, true, 1f, 0.5f), 1e-5f);
            Assert.AreEqual(0f, LandingGear.Step(0.1f, true, 1f, 0.5f), 1e-5f);
            Assert.AreEqual(1f, LandingGear.Step(0.9f, false, 1f, 0.5f), 1e-5f);
        }

        [Test]
        public void Pose_DownIsAsBaked_UpIsTurnedAndLifted()
        {
            var leg = new LandingGearLeg
            {
                DownPosition = new float3(1f, 2f, 3f),
                DownRotation = quaternion.identity,
                Axis = new float3(1f, 0f, 0f),
                Radians = math.PI / 2f,
                Lift = new float3(0f, 0.5f, 0f),
            };

            LandingGear.Pose(leg, 1f, out var position, out var rotation);
            Assert.AreEqual(0f, math.distance(position, leg.DownPosition), 1e-5f);
            Assert.AreEqual(1f, math.abs(math.dot(rotation.value, quaternion.identity.value)), 1e-5f);

            LandingGear.Pose(leg, 0f, out position, out rotation);
            Assert.AreEqual(0f, math.distance(position, new float3(1f, 2.5f, 3f)), 1e-5f);
            // +90 round +x: a leg that hangs down (-y) points back (-z), as Unity's Quaternion.AngleAxis.
            var down = math.mul(rotation, new float3(0f, -1f, 0f));
            Assert.AreEqual(0f, math.distance(down, new float3(0f, 0f, -1f)), 1e-4f);
        }
    }
}
