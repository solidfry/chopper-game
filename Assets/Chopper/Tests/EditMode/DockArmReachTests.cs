using Chopper.Presentation.Effects;
using NUnit.Framework;
using UnityEngine;

namespace Chopper.Tests
{
    public class DockArmReachTests
    {
        [TestCase(1.5f, -1.0f, 2.5f)]
        [TestCase(-2.0f, 0.5f, 1.0f)]
        [TestCase(0.3f, 2.6f, -0.5f)]
        [TestCase(3.0f, -2.0f, -1.5f)]
        public void Solve_PutsTheTipOnAReachableTarget(float x, float y, float z)
        {
            var target = new Vector3(x, y, z);
            DockArmReach.Solve(target, out var yaw, out var shoulder, out var elbow);
            Assert.That(Vector3.Distance(DockArmReach.Tip(yaw, shoulder, elbow), target), Is.LessThan(0.01f));
        }

        [Test]
        public void Solve_TooFar_PointsStraightAtIt()
        {
            var target = new Vector3(0f, 0f, 20f);
            DockArmReach.Solve(target, out var yaw, out var shoulder, out var elbow);
            var tip = DockArmReach.Tip(yaw, shoulder, elbow);
            Assert.That(Vector3.Angle(tip, target), Is.LessThan(1f));
            Assert.That(tip.magnitude, Is.EqualTo(DockArmReach.Upper + DockArmReach.Fore).Within(0.05f));
        }
    }
}
