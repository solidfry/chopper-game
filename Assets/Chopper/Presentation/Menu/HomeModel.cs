using System;
using System.Collections.Generic;
using Chopper.Presentation.Hangar;
using Chopper.Simulation.Session;
using Chopper.Simulation.Loadout;
using UnityEngine;

namespace Chopper.Presentation.Menu
{
    /// <summary>The home screen's pages: the main menu, and the pilot codex.</summary>
    public enum HomePage { Main, Pilots }

    /// <summary>
    /// View model of the home screen (the main menu, scene <see cref="SceneName"/>): which page shows, the profile card
    /// (your pilot, level), the pilot codex (every pilot, read-only, with SET AS PILOT) and the training map.
    /// Scene changes go through <see cref="Bridge.PresentationContext"/>; this model has no scene code.
    /// </summary>
    public sealed class HomeModel
    {
        public const string SceneName = "Home";

        private readonly LoadoutChoiceStore _store;
        private readonly Results.PilotProgress _progress;
        private readonly List<PilotCard> _pilots = new();
        private readonly List<MapCatalog.MapEntry> _trainingMaps = new();

        public HomeModel(LoadoutCatalog catalog, LoadoutChoiceStore store, MapCatalog maps, Results.PilotProgress progress = null)
        {
            _store = store;
            _progress = progress;
            if (catalog != null)
                foreach (var pilot in catalog.Pilots)
                    if (pilot != null) _pilots.Add(new PilotCard(pilot, locked: false));
            if (maps != null) _trainingMaps.AddRange(maps.Training());
            ShownPilot = Math.Max(0, IndexOf(SelectedPilotId));
        }

        /// <summary>Page changed, or the selected pilot changed (rebuild).</summary>
        public event Action Changed;
        /// <summary>Only the big codex card changed.</summary>
        public event Action ShownChanged;

        public HomePage Page { get; private set; } = HomePage.Main;

        /// <summary>A real map is up behind the menu (<see cref="HomeFlyover"/>): the view swaps its drawn grid for a CRT layer.</summary>
        public bool LiveBackdrop { get; private set; }

        /// <summary>The backdrop camera cut to a new shot (the view flashes a CRT glitch).</summary>
        public event Action ShotCut;

        public void NotifyCut() => ShotCut?.Invoke();

        /// <summary>A submenu (codex, options) is open: the backdrop blurs and darkens so the text reads (<see cref="HomeFlyover"/>).</summary>
        public bool Dimmed { get; private set; }

        /// <summary>Set by the view each frame; no event (the flyover fades towards it).</summary>
        public void SetDimmed(bool dimmed) => Dimmed = dimmed;

        /// <summary>The city image for the UI (low resolution, quantized), or null when the camera draws to the screen.</summary>
        public Texture BackdropTexture { get; private set; }

        /// <summary>A soft blurred copy of the city for the glass behind buttons; null without the pixel look.</summary>
        public Texture BlurTexture { get; private set; }

        public void SetLiveBackdrop(bool live, Texture texture = null, Texture blur = null)
        {
            if (live == LiveBackdrop && texture == BackdropTexture && blur == BlurTexture) return;
            LiveBackdrop = live;
            BackdropTexture = texture;
            BlurTexture = blur;
            Changed?.Invoke();
        }
        public IReadOnlyList<PilotCard> Pilots => _pilots;
        public int ShownPilot { get; private set; }
        public PilotCard? Shown => ShownPilot >= 0 && ShownPilot < _pilots.Count ? _pilots[ShownPilot] : null;

        public ushort SelectedPilotId => _store != null ? _store.Pilot : (ushort)0;
        /// <summary>Your pilot (the one saved for the next spawn); the first one when none is saved yet.</summary>
        public PilotCard? SelectedPilot
        {
            get
            {
                var index = IndexOf(SelectedPilotId);
                if (index < 0) index = 0;
                return index < _pilots.Count ? _pilots[index] : null;
            }
        }

        /// <summary>Player name on the profile card. Steam names later (plan: progression, Steam).</summary>
        public string OperatorName => "OPERATOR";
        /// <summary>From the local XP total (<see cref="Results.PilotProgress"/>); level 1 and an empty bar without it.</summary>
        public int Level => _progress != null ? Results.PilotLevels.Level(_progress.TotalXp, out _, out _) : 1;
        public float LevelProgress
        {
            get
            {
                if (_progress == null) return 0f;
                Results.PilotLevels.Level(_progress.TotalXp, out var into, out var needed);
                return needed > 0 ? (float)into / needed : 0f;
            }
        }

        public bool HasTrainingMap => TrainingMap != null;
        /// <summary>Scene of <see cref="TrainingMap"/>; null when none.</summary>
        public string TrainingScene => TrainingMap?.Scene;

        /// <summary>The saved training map, else the first one. Null when no map is set up.</summary>
        private MapCatalog.MapEntry TrainingMap
        {
            get
            {
                var saved = _store?.TrainingMap;
                var found = _trainingMaps.Find(m => m.Scene == saved);
                return found ?? (_trainingMaps.Count > 0 ? _trainingMaps[0] : null);
            }
        }

        public string TrainingMapName
        {
            get
            {
                var map = TrainingMap;
                if (map == null) return "";
                return (string.IsNullOrEmpty(map.DisplayName) ? map.Scene : map.DisplayName).ToUpperInvariant();
            }
        }

        public void ShowPage(HomePage page)
        {
            if (page == Page) return;
            Page = page;
            if (page == HomePage.Pilots) ShownPilot = Math.Max(0, IndexOf(SelectedPilotId));
            Changed?.Invoke();
        }

        /// <summary>Back out of a page. False when already on the main page.</summary>
        public bool Back()
        {
            if (Page == HomePage.Main) return false;
            ShowPage(HomePage.Main);
            return true;
        }

        public void ShowPilot(int index)
        {
            if (index < 0 || index >= _pilots.Count || index == ShownPilot) return;
            ShownPilot = index;
            ShownChanged?.Invoke();
        }

        /// <summary>Next / previous pilot in the codex (wraps).</summary>
        public void StepPilot(int delta)
        {
            if (_pilots.Count == 0) return;
            ShowPilot(((ShownPilot + delta) % _pilots.Count + _pilots.Count) % _pilots.Count);
        }

        /// <summary>Saves the shown pilot for the next spawn (as LOCK IN in the hangar does).</summary>
        public bool SelectShown()
        {
            var shown = Shown;
            if (shown == null || shown.Value.Locked || _store == null) return false;
            _store.Pilot = shown.Value.Id;
            Changed?.Invoke();
            return true;
        }

        public int IndexOf(ushort id) => _pilots.FindIndex(p => p.Id == id);

        /// <summary>"01".."99": codex serial of a pilot.</summary>
        public static string Serial(int index) => (index + 1).ToString("00");
    }
}
