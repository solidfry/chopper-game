using Chopper.Presentation.Hud;
using NUnit.Framework;
using UnityEngine;

namespace Chopper.Tests
{
    public sealed class HudSphereTests
    {
        private const float Aspect = 16f / 9f;
        private static readonly float Angle = 32f * Mathf.Deg2Rad;

        [Test]
        public void Flat_ChangesNothing()
        {
            var point = new Vector2(0.2f, 0.9f);
            Assert.That(HudSphere.ToHud(point, 0f, Aspect), Is.EqualTo(point));
            Assert.That(HudSphere.Inset(0f, Aspect), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void TheCentreStays_AndTheTopEdgeIsThePanelEdge()
        {
            var centre = HudSphere.ToHud(new Vector2(0.5f, 0.5f), Angle, Aspect);
            Assert.That(Vector2.Distance(centre, new Vector2(0.5f, 0.5f)), Is.LessThan(1e-5f));
            var top = HudSphere.ToHud(new Vector2(0.5f, 1f), Angle, Aspect);
            Assert.That(top.y, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(top.x, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [TestCase(0f, 0f)]
        [TestCase(1f, 1f)]
        [TestCase(0.5f, 0f)]
        [TestCase(1f, 0.5f)]
        [TestCase(0f, 0.93f)]
        public void EveryScreenPoint_IsOnThePanel_SoAMarkerAtTheEdgeIsNotLost(float x, float y)
        {
            var hud = HudSphere.ToHud(new Vector2(x, y), Angle, Aspect);
            Assert.That(hud.x, Is.InRange(-1e-5f, 1f + 1e-5f));
            Assert.That(hud.y, Is.InRange(-1e-5f, 1f + 1e-5f));
        }

        [Test]
        public void TheHudGrowsTowardsTheEdge_SoNumbersFanOut()
        {
            // Panel distance per screen distance: smaller means drawn bigger.
            const float step = 0.01f;
            float Density(float x) =>
                (HudSphere.ToHud(new Vector2(x + step, 0.5f), Angle, Aspect).x - HudSphere.ToHud(new Vector2(x, 0.5f), Angle, Aspect).x) / step;
            Assert.That(Density(0.9f), Is.LessThan(Density(0.7f)));
            Assert.That(Density(0.7f), Is.LessThan(Density(0.5f)));
        }

        [Test]
        public void Inset_KeepsCornerTextOnScreen()
        {
            var inset = HudSphere.Inset(Angle, Aspect);
            Assert.That(inset.x, Is.GreaterThan(0f).And.LessThan(0.2f));
            Assert.That(inset.y, Is.GreaterThan(0f).And.LessThan(0.2f));
        }
    }
}
