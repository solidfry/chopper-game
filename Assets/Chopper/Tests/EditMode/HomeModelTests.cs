using System.Collections.Generic;
using Chopper.Presentation.Hangar;
using Chopper.Presentation.Menu;
using Chopper.Simulation.Loadout;
using NUnit.Framework;
using UnityEngine;

namespace Chopper.Tests
{
    public sealed class HomeModelTests
    {
        private readonly List<Object> _created = new();

        private sealed class MemoryPrefs : IHangarPrefs
        {
            private readonly Dictionary<string, string> _strings = new();
            private readonly Dictionary<string, int> _ints = new();
            public string GetString(string key, string fallback) => _strings.TryGetValue(key, out var value) ? value : fallback;
            public int GetInt(string key, int fallback) => _ints.TryGetValue(key, out var value) ? value : fallback;
            public void SetString(string key, string value) => _strings[key] = value;
            public void SetInt(string key, int value) => _ints[key] = value;
            public void Save() { }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _created) Object.DestroyImmediate(asset);
            _created.Clear();
        }

        private LoadoutCatalog Catalog(params (ushort Id, string Sign)[] pilots)
        {
            var list = new List<PilotDefinition>();
            foreach (var (id, sign) in pilots)
            {
                var pilot = ScriptableObject.CreateInstance<PilotDefinition>();
                pilot.Initialise(id, sign, "ROLE", "Passive.", "ABILITY", "Text.", Color.green, new PilotModifiers(), 0);
                _created.Add(pilot);
                list.Add(pilot);
            }
            var catalog = ScriptableObject.CreateInstance<LoadoutCatalog>();
            catalog.SetPilots(list.ToArray());
            _created.Add(catalog);
            return catalog;
        }

        [Test]
        public void Codex_OpensOnYourPilot_StepsAndWraps_AndBackReturnsToMain()
        {
            var store = new LoadoutChoiceStore(new MemoryPrefs()) { Pilot = 2 };
            var model = new HomeModel(Catalog((1, "APOTHECARY"), (2, "VIPER"), (3, "PYRE")), store, null);

            Assert.That(model.SelectedPilot?.CallSign, Is.EqualTo("VIPER"));
            model.ShowPage(HomePage.Pilots);
            Assert.That(model.Page, Is.EqualTo(HomePage.Pilots));
            Assert.That(model.Shown?.CallSign, Is.EqualTo("VIPER"), "codex opens on your pilot");

            model.StepPilot(1);
            model.StepPilot(1);
            Assert.That(model.Shown?.CallSign, Is.EqualTo("APOTHECARY"), "wraps forward");
            model.StepPilot(-1);
            Assert.That(model.Shown?.CallSign, Is.EqualTo("PYRE"), "wraps back");

            Assert.That(model.Back(), Is.True);
            Assert.That(model.Page, Is.EqualTo(HomePage.Main));
            Assert.That(model.Back(), Is.False, "already on the main page");
        }

        [Test]
        public void SelectShown_SavesThePilot_AndNoPilotFallsBackToTheFirst()
        {
            var store = new LoadoutChoiceStore(new MemoryPrefs());
            var model = new HomeModel(Catalog((1, "APOTHECARY"), (2, "VIPER")), store, null);
            Assert.That(model.SelectedPilot?.CallSign, Is.EqualTo("APOTHECARY"), "none saved: the first");
            Assert.That(model.HasTrainingMap, Is.False);

            model.ShowPage(HomePage.Pilots);
            model.ShowPilot(1);
            Assert.That(model.SelectShown(), Is.True);
            Assert.That(store.Pilot, Is.EqualTo(2));
            Assert.That(model.SelectedPilot?.CallSign, Is.EqualTo("VIPER"));
        }

        private GameModeCatalog Modes()
        {
            var modes = ScriptableObject.CreateInstance<GameModeCatalog>();
            _created.Add(modes);
            modes.Add(new GameModeCatalog.ModeEntry { Id = "quickplay", DisplayName = "Quick Play", Available = true });
            modes.Add(new GameModeCatalog.ModeEntry { Id = "ffa", DisplayName = "Free For All", Available = true, Queue = "ffa-queue" });
            modes.Add(new GameModeCatalog.ModeEntry { Id = "tdm", DisplayName = "Team Deathmatch", Available = false });
            return modes;
        }

        [Test]
        public void Modes_OpenOnTheSavedMode_StepAndWrap_AndOnlyPlayableModesAreSaved()
        {
            var store = new LoadoutChoiceStore(new MemoryPrefs()) { MatchMode = "ffa" };
            var model = new HomeModel(Catalog((1, "VIPER")), store, null, modes: Modes());

            Assert.That(model.HasModes, Is.True);
            Assert.That(model.SelectedMode?.Id, Is.EqualTo("ffa"));
            model.ShowPage(HomePage.Modes);
            Assert.That(model.ShownModeEntry?.Id, Is.EqualTo("ffa"), "opens on the saved mode");

            model.StepMode(1);
            Assert.That(model.ShownModeEntry?.Id, Is.EqualTo("tdm"));
            Assert.That(model.PickShownMode(), Is.False, "an OFFLINE mode cannot be picked");
            Assert.That(store.MatchMode, Is.EqualTo("ffa"));

            model.StepMode(1);
            Assert.That(model.ShownModeEntry?.Id, Is.EqualTo("quickplay"), "wraps forward");
            Assert.That(model.PickShownMode(), Is.True);
            Assert.That(store.MatchMode, Is.EqualTo("quickplay"));
            Assert.That(model.Back(), Is.True);
            Assert.That(model.Page, Is.EqualTo(HomePage.Main));
        }

        [Test]
        public void Modes_UnknownOrOfflineSavedModeFallsBackToTheFirstPlayable_AndQueueFallsBack()
        {
            var modes = Modes();
            Assert.That(modes.Resolve("tdm")?.Id, Is.EqualTo("quickplay"), "offline: the first playable");
            Assert.That(modes.Resolve("gone")?.Id, Is.EqualTo("quickplay"));
            Assert.That(GameModeCatalog.QueueOf(modes.Find("quickplay"), "Default"), Is.EqualTo("Default"));
            Assert.That(GameModeCatalog.QueueOf(modes.Find("FFA"), "Default"), Is.EqualTo("ffa-queue"), "ids ignore case");
            Assert.That(GameModeCatalog.QueueOf(null, "Default"), Is.EqualTo("Default"));

            var model = new HomeModel(Catalog((1, "VIPER")), new LoadoutChoiceStore(new MemoryPrefs()), null);
            Assert.That(model.HasModes, Is.False, "no catalog: PLAY goes straight to the hangar");
        }
    }
}
