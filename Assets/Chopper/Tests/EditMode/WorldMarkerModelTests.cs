using Chopper.Presentation.Hud;
using NUnit.Framework;
using UnityEngine;

namespace Chopper.Tests
{
    public sealed class WorldMarkerModelTests
    {
        [Test]
        public void Place_OnScreenKeepsThePoint()
        {
            Assert.That(WorldMarkerModel.Place(new Vector3(0.3f, 0.7f, 50f), out var position, out _), Is.True);
            Assert.That(position.x, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(position.y, Is.EqualTo(0.7f).Within(1e-5f));
        }

        [Test]
        public void Place_OffScreenRightGoesToTheRightEdgeWithArrowRight()
        {
            Assert.That(WorldMarkerModel.Place(new Vector3(2f, 0.5f, 50f), out var position, out var angle), Is.False);
            Assert.That(position.x, Is.EqualTo(1f - WorldMarkerModel.EdgeMargin).Within(1e-5f));
            Assert.That(position.y, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(angle, Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void Place_BehindTheCameraIsMirrored()
        {
            // Behind and to the right projects to the left side of the viewport.
            Assert.That(WorldMarkerModel.Place(new Vector3(0.2f, 0.5f, -10f), out var position, out var angle), Is.False);
            Assert.That(position.x, Is.GreaterThan(0.5f));
            Assert.That(angle, Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void Add_StopsAtCapacityAndClearRaisesChanged()
        {
            var model = new WorldMarkerModel();
            model.Begin();
            for (var i = 0; i < WorldMarkerModel.Capacity; i++)
                Assert.That(model.Add(new WorldMarker()), Is.True);
            Assert.That(model.Add(new WorldMarker()), Is.False);
            model.End();
            Assert.That(model.Count, Is.EqualTo(WorldMarkerModel.Capacity));

            var changed = 0;
            model.Changed += () => changed++;
            model.Clear();
            Assert.That(model.Count, Is.EqualTo(0));
            Assert.That(changed, Is.EqualTo(1));
        }
    }
}
