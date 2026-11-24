using Chopper.Simulation.Match;
using NUnit.Framework;
using Unity.Mathematics;

namespace Chopper.Tests.EditMode
{
    /// <summary>The nuke's shock front throws choppers away from the blast (owner, 24 Nov).</summary>
    public class NukeShockTests
    {
        [Test]
        public void Front_RunsAtTheSpeedOfSound()
        {
            Assert.AreEqual(0f, NukeShock.Front(-1f));
            Assert.AreEqual(340f, NukeShock.Front(1f), 0.01f);
        }

        [Test]
        public void Push_IsAwayFromTheBlastAndUp()
        {
            var push = NukeShock.Push(float3.zero, new float3(200f, 20f, 0f));
            Assert.Greater(push.x, 0f, "away from the blast");
            Assert.Greater(push.y, 0f, "lifts a little");
            Assert.AreEqual(0f, push.z, 0.001f);
        }

        [Test]
        public void Push_IsStrongerNearAndZeroOutside()
        {
            var near = math.length(NukeShock.Push(float3.zero, new float3(100f, 10f, 0f)));
            var far = math.length(NukeShock.Push(float3.zero, new float3(600f, 10f, 0f)));
            Assert.Greater(near, far);
            Assert.LessOrEqual(near, NukeShock.MaxPush);
            Assert.AreEqual(0f, math.length(NukeShock.Push(float3.zero, new float3(NukeShock.Radius + 1f, 0f, 0f))));
        }

        [Test]
        public void Push_RightAtTheBlastGoesUp()
        {
            var push = NukeShock.Push(float3.zero, float3.zero);
            Assert.Greater(push.y, 0f);
        }
    }
}
