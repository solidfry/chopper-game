using Chopper.Simulation.Bots;
using Chopper.Simulation.Match;
using NUnit.Framework;

namespace Chopper.Tests
{
    /// <summary>A bot carrier hovering over a dock must be low enough to set the nuke down (owner, 28 Nov 2026).</summary>
    public sealed class CarrierDockHeightTests
    {
        [Test]
        public void CarrierHoverAndFloor_LetTheNukeReachTheDock()
        {
            Assert.That(BotObjective.DockHoverHeight - NukeZones.HangBelow, Is.LessThanOrEqualTo(NukeZones.DockHeight));
            Assert.That(BotObjective.DockFloorClearance, Is.LessThan(BotObjective.DockHoverHeight));
            Assert.That(BotObjective.DockDescentRange, Is.GreaterThan(NukeZones.DockRadius));
        }
    }
}
