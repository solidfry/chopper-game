using System.Collections.Generic;
using Chopper.Presentation.Visuals;
using NUnit.Framework;
using Unity.Mathematics;

namespace Chopper.Tests
{
    public sealed class LavaFogPlacementTests
    {
        [Test]
        public void Points_EverySpacing_AcrossCorners_EndsOnTheEnd()
        {
            var line = new List<float3> { new(0, 0, 0), new(30, 0, 0), new(30, 0, 50) };
            var points = new List<float3>();
            LavaFogPlacement.Points(line, 20f, points);
            // 0, 20, then 10 m past the corner (30,0,10), (30,0,30), (30,0,50).
            Assert.That(points.Count, Is.EqualTo(5));
            Assert.That(math.distance(points[2], new float3(30, 0, 10)), Is.LessThan(1e-3f));
            Assert.That(math.distance(points[4], new float3(30, 0, 50)), Is.LessThan(1e-3f));
        }

        [Test]
        public void Points_ShortOrEmpty()
        {
            var points = new List<float3>();
            LavaFogPlacement.Points(new List<float3>(), 10f, points);
            Assert.That(points, Is.Empty);
            LavaFogPlacement.Points(new List<float3> { new(1, 2, 3) }, 10f, points);
            Assert.That(points.Count, Is.EqualTo(1));
        }
    }
}
