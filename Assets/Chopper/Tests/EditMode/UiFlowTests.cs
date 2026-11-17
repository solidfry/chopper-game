using System.Collections.Generic;
using System.Linq;
using Chopper.Presentation.Flow;
using Chopper.Presentation.Hud;
using Chopper.Simulation.Input;
using Chopper.Simulation.Visuals;
using NUnit.Framework;

namespace Chopper.Tests
{
    /// <summary>Load-screen briefing text and the controls page rows.</summary>
    public sealed class UiFlowTests
    {
        [Test]
        public void BriefingConditions_ReadAsTimeAndWeather()
        {
            Assert.That(BriefingConditionsSystem.Text(TimeOfDay.Night, Weather.Rain), Is.EqualTo("NIGHT // RAIN"));
            Assert.That(BriefingConditionsSystem.Text(TimeOfDay.Day, Weather.Clear), Is.EqualTo("DAY // CLEAR SKY"));
            Assert.That(BriefingConditionsSystem.Text(TimeOfDay.Morning, Weather.Ash), Is.EqualTo("MORNING // ASHFALL"));
        }

        [Test]
        public void ControlRows_CoverEveryGroupWithKeys()
        {
            var controls = new PlayerControls();
            try
            {
                var prompts = new InputPrompts(controls);
                var rows = new List<InputPrompts.ControlRow>();
                prompts.ControlRows(rows);
                Assert.That(rows.Select(r => r.Group).Distinct(), Is.EqualTo(new[] { "FLIGHT", "WEAPONS", "SYSTEMS", "VIEW", "MENUS" }));
                var repair = rows.Single(r => r.Label == "REPAIR (HOLD)");
                Assert.That(repair.Keyboard, Is.EqualTo("R"));
                Assert.That(repair.Pad, Is.Not.EqualTo("-"));
                var thrust = rows.Single(r => r.Label == "THRUST UP / DOWN");
                Assert.That(thrust.Pad, Does.StartWith("LEFT STICK"));
                Assert.That(rows.Single(r => r.Label == "PRIMARY WEAPON").Keyboard, Does.Contain("LMB"));
                Assert.That(rows.Single(r => r.Label == "DIVE").Pad, Is.EqualTo("-"));
            }
            finally
            {
                // Dispose() calls Object.Destroy, which logs an error in edit mode.
                UnityEngine.Object.DestroyImmediate(controls.asset);
            }
        }
    }
}
