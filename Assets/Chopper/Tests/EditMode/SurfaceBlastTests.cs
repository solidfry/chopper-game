using Chopper.Presentation.Effects;
using Chopper.Simulation.Visuals;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class SurfaceBlastTests
    {
        [Test]
        public void EachSurface_ThrowsUpItsOwnMaterial()
        {
            var ground = SurfaceBlast.For(SurfaceKind.Ground, SurfaceBlast.StandardRadius);
            var concrete = SurfaceBlast.For(SurfaceKind.Concrete, SurfaceBlast.StandardRadius);
            var metal = SurfaceBlast.For(SurfaceKind.Metal, SurfaceBlast.StandardRadius);
            var water = SurfaceBlast.For(SurfaceKind.Water, SurfaceBlast.StandardRadius);

            Assert.That(ground.UseMapDust, Is.True, "ground dust is the map's sand or ash");
            Assert.That(ground.Sparks, Is.Zero, "dirt does not spark");
            Assert.That(concrete.Sparks, Is.GreaterThan(0), "concrete chips spark a little");
            Assert.That(metal.Sparks, Is.GreaterThan(concrete.Sparks), "metal is mostly sparks");
            Assert.That(metal.Dust, Is.LessThan(concrete.Dust), "metal throws little dust");
            Assert.That(water.Spray, Is.GreaterThan(ground.Spray), "water is a column of spray");
            Assert.That(water.SpraySpread, Is.LessThan(ground.SpraySpread), "the water column is narrower");
        }

        [Test]
        public void BiggerBlasts_ThrowUpMore_ButStayCapped()
        {
            var small = SurfaceBlast.For(SurfaceKind.Ground, 4f);
            var standard = SurfaceBlast.For(SurfaceKind.Ground, SurfaceBlast.StandardRadius);
            var bomb = SurfaceBlast.For(SurfaceKind.Ground, 20f);
            var huge = SurfaceBlast.For(SurfaceKind.Ground, 500f);

            Assert.That(small.Spray, Is.LessThan(standard.Spray));
            Assert.That(bomb.Spray, Is.GreaterThan(standard.Spray));
            Assert.That(bomb.SpraySpeed, Is.GreaterThan(standard.SpraySpeed));
            Assert.That(huge.Spray, Is.LessThanOrEqualTo(standard.Spray * 3), "a huge blast must not empty the particle budget");
            Assert.That(huge.Scale, Is.EqualTo(2.5f));
        }

        [Test]
        public void NoRadius_UsesTheStandardScale()
        {
            Assert.That(SurfaceBlast.ScaleFor(0f), Is.EqualTo(1f), "bomblets have no radius in the visual");
            Assert.That(SurfaceBlast.ScaleFor(SurfaceBlast.StandardRadius), Is.EqualTo(1f));
        }
    }
}
