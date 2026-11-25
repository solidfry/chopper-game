using Chopper.Presentation.Hud;
using Chopper.Simulation.Combat;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class ReticleTests
    {
        [Test]
        public void EachWeaponType_HasItsOwnReticle()
        {
            Assert.AreEqual(ReticleKind.Missile, Reticles.Of(new PylonWeapon { Behaviour = PylonBehaviour.Guided, RequiresLock = true }));
            Assert.AreEqual(ReticleKind.Drone, Reticles.Of(new PylonWeapon { Behaviour = PylonBehaviour.Drone }));
            Assert.AreEqual(ReticleKind.Rocket, Reticles.Of(new PylonWeapon { Behaviour = PylonBehaviour.Rocket }));
            Assert.AreEqual(ReticleKind.Bomb, Reticles.Of(new PylonWeapon { Behaviour = PylonBehaviour.Rocket, Gravity = 9.81f }));
            Assert.AreEqual(ReticleKind.Mine, Reticles.Of(new PylonWeapon { Behaviour = PylonBehaviour.Rocket, Drag = 2f }));

            Assert.AreEqual("", Reticles.ClassOf(ReticleKind.Gun), "The gun ring is the plain reticle.");
            var seen = new System.Collections.Generic.HashSet<string>();
            for (var kind = ReticleKind.Missile; kind <= ReticleKind.Drone; kind++)
                Assert.IsTrue(seen.Add(Reticles.ClassOf(kind)), $"{kind}: own class");
        }
    }
}
