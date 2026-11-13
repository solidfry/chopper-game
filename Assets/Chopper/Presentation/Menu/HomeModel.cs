using System;
using System.Collections.Generic;
using Chopper.Presentation.Hangar;
using Chopper.Presentation.Profile;
using Chopper.Simulation.Session;
using Chopper.Simulation.Loadout;
using UnityEngine;

namespace Chopper.Presentation.Menu
{
    /// <summary>The home screen's pages: the main menu, the pilot codex, the player's profile, and mode select (PLAY).</summary>
    public enum HomePage { Main, Pilots, Profile, Modes }

    /// <summary>
    /// View model of the home screen (the main menu, scene <see cref="SceneName"/>): which page shows, the profile card
    /// (your pilot, level), the pilot codex (every pilot, read-only, with SET AS PILOT) and the training map.
    /// Scene changes go through <see cref="Bridge.PresentationContext"/>; this model has no scene code.
    /// </summary>
    public sealed class HomeModel
    {
        public const string SceneName = "Home";

        private readonly LoadoutChoiceStore _store;
        private readonly ProfileService _profile;
        private readonly IPlayerIdentity _identity;
        private readonly List<PilotCard> _pilots = new();
        private readonly List<MapCatalog.MapEntry> _trainingMaps = new();
        private readonly GameModeCatalog _modeCatalog;
        private readonly List<GameModeCatalog.ModeEntry> _modes = new();

        public HomeModel(LoadoutCatalog catalog, LoadoutChoiceStore store, MapCatalog maps, ProfileService profile = null,
            IPlayerIdentity identity = null, GameModeCatalog modes = null)
        {
            _modeCatalog = modes;
            if (modes != null)
                foreach (var mode in modes.Modes)
                    if (mode != null) _modes.Add(mode);
            _store = store;
            _profile = profile;
            _identity = identity;
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
        /// <summary>Only the shown mode on the PLAY page changed.</summary>
        public event Action ModeShownChanged;

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
        public string OperatorName => _identity != null ? _identity.DisplayName : "OPERATOR";
        /// <summary>The player's lifetime record (null in tests without one).</summary>
        public ProfileService Profile => _profile;

        /// <summary>Mastery for the profile page: every chassis class, then every pilot. Empty without a profile.</summary>
        public List<TrackProgress> MasteryTracks()
        {
            var tracks = new List<TrackProgress>();
            if (_profile == null) return tracks;
            tracks.AddRange(_profile.ChassisTracks());
            foreach (var pilot in _pilots) tracks.Add(_profile.PilotProgress(pilot.Id, pilot.CallSign));
            return tracks;
        }
        public IPlayerIdentity Identity => _identity;
        /// <summary>From the profile's XP; level 1 and an empty bar without one.</summary>
        public int Level => _profile != null ? _profile.Level : 1;
        public float LevelProgress => _profile != null ? _profile.LevelProgress : 0f;

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
            if (page == HomePage.Modes) ShownMode = Math.Max(0, _modes.IndexOf(SelectedMode));
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

        // ---- Mode select (PLAY) ----

        /// <summary>Every mode in catalog order, playable or not (OFFLINE cards show what is planned).</summary>
        public IReadOnlyList<GameModeCatalog.ModeEntry> Modes => _modes;
        /// <summary>PLAY has a page to show (a catalog with a playable mode); without one PLAY goes straight to the hangar.</summary>
        public bool HasModes => SelectedMode != null;
        public int ShownMode { get; private set; }
        public GameModeCatalog.ModeEntry ShownModeEntry => ShownMode >= 0 && ShownMode < _modes.Count ? _modes[ShownMode] : null;

        /// <summary>The saved mode when it can be played, else the first playable one; null without a catalog.</summary>
        public GameModeCatalog.ModeEntry SelectedMode => _modeCatalog != null ? _modeCatalog.Resolve(_store?.MatchMode) : null;

        public void ShowMode(int index)
        {
            if (index < 0 || index >= _modes.Count || index == ShownMode) return;
            ShownMode = index;
            ModeShownChanged?.Invoke();
        }

        /// <summary>Next / previous mode card (wraps).</summary>
        public void StepMode(int delta)
        {
            if (_modes.Count == 0) return;
            ShowMode(((ShownMode + delta) % _modes.Count + _modes.Count) % _modes.Count);
        }

        /// <summary>Saves the shown mode for FIND MATCH. False when it is OFFLINE (not built yet) or nothing is shown.</summary>
        public bool PickShownMode()
        {
            var shown = ShownModeEntry;
            if (shown == null || !shown.Available || _store == null) return false;
            _store.MatchMode = shown.Id;
            return true;
        }

        /// <summary>"01".."99": codex serial of a pilot.</summary>
        public static string Serial(int index) => (index + 1).ToString("00");
    }
}
