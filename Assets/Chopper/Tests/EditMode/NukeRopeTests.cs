using Chopper.Presentation.Effects;
using NUnit.Framework;
using UnityEngine;

namespace Chopper.Tests
{
    public class NukeRopeTests
    {
        private static readonly Vector3 Winch = new(0f, 50f, 0f);
        private static readonly Vector3 Eye = new(0f, 44f, 0f);

        [Test]
        public void Ends_AreTheWinchAndTheHookEye_WhateverTheBow()
        {
            var bow = NukeRopeShape.Bow(new Vector3(40f, 0f, 0f), 0.5f, 0.03f, 1.2f);
            var points = new Vector3[8];
            NukeRopeShape.Fill(Winch, Eye, bow, 1f, points);
            Assert.That(Vector3.Distance(points[0], Winch), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(points[7], Eye), Is.LessThan(1e-4f));
        }

        [Test]
        public void Middle_SagsDown_AndBowsBackAgainstTheFlight()
        {
            var bow = NukeRopeShape.Bow(new Vector3(20f, 5f, 0f), 0.2f, 0.03f, 1.2f);
            var middle = NukeRopeShape.Point(Winch, Eye, bow, 0.5f);
            Assert.That(middle.x, Is.EqualTo(-0.6f).Within(1e-4f), "flying +x: the middle trails to -x");
            Assert.That(middle.y, Is.EqualTo(47f - 0.2f).Within(1e-4f), "climb speed does not bow it");
        }

        [Test]
        public void Bow_IsCappedAtMaxTrail()
        {
            var bow = NukeRopeShape.Bow(new Vector3(0f, 0f, 200f), 0f, 0.03f, 1.2f);
            Assert.That(bow.magnitude, Is.EqualTo(1.2f).Within(1e-4f));
            Assert.That(bow.z, Is.LessThan(0f));
        }

        [Test]
        public void Lowering_LineEndsPartWayDown()
        {
            var points = new Vector3[5];
            NukeRopeShape.Fill(Winch, Eye, Vector3.zero, 0.5f, points);
            Assert.That(points[4].y, Is.EqualTo(47f).Within(1e-4f));
            NukeRopeShape.Fill(Winch, Eye, Vector3.zero, 0f, points);
            Assert.That(Vector3.Distance(points[4], Winch), Is.LessThan(1e-4f), "no hook progress: no line out");
        }
    }
}
