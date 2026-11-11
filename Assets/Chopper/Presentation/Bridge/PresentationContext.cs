using System;
using System.Collections.Generic;
using Chopper.Presentation.Audio;
using Chopper.Presentation.Camera;
using Chopper.Presentation.Config;
using Chopper.Presentation.Effects;
using Chopper.Presentation.Events;
using Chopper.Presentation.Feedback;
using Chopper.Presentation.Flow;
using Chopper.Presentation.Hangar;
using Chopper.Presentation.Hud;
using Chopper.Presentation.Profile;
using Chopper.Presentation.Results;
using UnityEngine;

namespace Chopper.Presentation.Bridge
{
    /// <summary>
    /// Everything presentation needs, in one object: config, audio, effects, HUD view models and look, camera and the event
    /// channel. Made by the scene's <see cref="PresentationBridge"/>; ECS presentation systems get it through
    /// <see cref="PresentationSystemBase"/>, UI through the bridge. Ticks all of them from one place.
    /// TODO: becomes the match <c>LifetimeScope</c> (VContainer) registrations.
    /// </summary>
    public sealed class PresentationContext : IDisposable
    {
        private readonly AudioService _audio;
        private readonly List<IDisposable> _owned = new();
        private readonly UnityEngine.SceneManagement.Scene _scene;
        private HangarModel _hangar;
        private Menu.HomeModel _home;
        private SceneFlow _flow;
        private MusicLibrary.Set _musicSet;
        private AudioClip _musicOverride;
        private bool _musicStarted;
        private float _musicLevel = -1f;
        private SoundLoop _ambience;
        /// <summary>0 = chase view, 1 = cockpit view; moves slowly, so the sound changes with the camera blend.</summary>
        private float _cockpitMuffle;
        /// <summary>Only <c>Flight/Scoreboard</c> is on here; the flight actions are read by the simulation.</summary>
        private readonly Simulation.Input.PlayerControls _controls = new();

        public PresentationContext(GameConfig config, Transform root, ChopperCameraRig cameraRig)
        {
            Config = config;
            _scene = root != null ? root.gameObject.scene : default;
            CameraRig = cameraRig;
            Events = new PresentationEvents();
            _audio = new AudioService(config != null ? config.Audio : null, root);
            Effects = new CombatEffects(config != null ? config.Effects : null, root);
            var moduleRoot = new GameObject("Module Visuals").transform;
            moduleRoot.SetParent(root, false);
            ModuleVisuals = new EffectPool(moduleRoot);
            HudLook = new HudLook(config != null ? config.HudLook : null, Hud, Events);
            PilotPortrait = new Pilots.PilotPortraitRig(config != null && config.HudLook != null ? config.HudLook.PilotPortrait : null);
            Profile = new ProfileService(new PrefsProfileStore(new PlayerPrefsHangarPrefs()));
            Results = new MatchResultsModel(Match, Profile);
            if (cameraRig != null && config != null) cameraRig.UseSettings(config.Camera);

            _owned.Add(new CombatFeedback(this));
            _owned.Add(new MatchFeedback(this));
            _owned.Add(new TargetingFeedback(this));
            _controls.Flight.Scoreboard.Enable();
            Prompts = new InputPrompts(_controls);
            Prompts.Changed += ApplyPromptKeys;
            ApplyPromptKeys();
        }

        /// <summary>Key names for on-screen prompts, for the input device in use.</summary>
        public InputPrompts Prompts { get; }

        private void ApplyPromptKeys() => Hud.SetPromptKeys(Prompts.Key("Repair"), Prompts.Key("Ability"));

        public GameConfig Config { get; }
        public IAudioService Audio => _audio;
        /// <summary>Can be null: cues are then not played.</summary>
        public SoundLibrary Sounds => Config != null ? Config.Audio : null;
        public CombatEffects Effects { get; }
        /// <summary>Pool for module prefabs fitted to chassis sockets (guns, pods, rotors...).</summary>
        public EffectPool ModuleVisuals { get; }
        public ChopperHudModel Hud { get; } = new();
        public MatchHudModel Match { get; } = new();
        /// <summary>End-of-match screen: stats, awards, XP (<see cref="Feedback.MatchFeedback"/> fills it).</summary>
        public MatchResultsModel Results { get; }
        /// <summary>The player's lifetime record: XP, totals, awards, achievements (local saves until Cloud Save).</summary>
        public ProfileService Profile { get; }
        /// <summary>Name and avatar (local now, Steam later).</summary>
        public IPlayerIdentity Identity { get; } = new LocalIdentity();
        /// <summary>Screen markers on other choppers and ground targets, and the rocket lead indicator.</summary>
        public WorldMarkerModel Markers { get; } = new();
        /// <summary>Retro HUD look: the HUD view attaches its document; gameplay drives glitches.</summary>
        public HudLook HudLook { get; }
        /// <summary>
        /// The live pilot portrait: the local pilot in a match (<see cref="Hud.LocalPilotPortraitSystem"/>), the shown
        /// pilot on the select screen. Its texture is null without the shader (Setup step 1).
        /// </summary>
        public Pilots.PilotPortraitRig PilotPortrait { get; }
        /// <summary>Can be null (no chase camera in the scene).</summary>
        public ChopperCameraRig CameraRig { get; }
        public PresentationEvents Events { get; }
        /// <summary>A menu (options) is open: flight input is blocked (<see cref="InputSettingsSystem"/>).</summary>
        public bool MenuOpen { get; set; }
        /// <summary>Saved control options (mouse steer, sensitivity, invert).</summary>
        public InputOptions InputOptions { get; } = new();

        /// <summary>Player option: how solid each part of the HUD is. Saved.</summary>
        public HudOpacity HudOpacity { get; } = new();
        /// <summary>The hangar's saved loadout and skin (read again by the client world when a match starts).</summary>
        public LoadoutChoiceStore LoadoutChoices { get; } = new();
        /// <summary>Hangar view model; made on first use (null without a loadout catalog). Only the hangar scene uses it.</summary>
        public HangarModel Hangar => _hangar ??= Config != null && Config.LoadoutCatalog != null
            ? new HangarModel(Config.LoadoutCatalog, Config.Skins, LoadoutChoices, new AllUnlocked(), Config.Maps)
            : null;
        /// <summary>Home screen view model; made on first use (null without a loadout catalog). Only the home scene uses it.</summary>
        public Menu.HomeModel Home => _home ??= Config != null && Config.LoadoutCatalog != null
            ? new Menu.HomeModel(Config.LoadoutCatalog, LoadoutChoices, Config.Maps, Profile, Identity, Config.Modes)
            : null;
        public bool IsDisposed { get; private set; }

        /// <summary>The home scene is in the build and not the open one.</summary>
        public bool CanOpenHome => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != Menu.HomeModel.SceneName &&
                                   Application.CanStreamedLevelBeLoaded(Menu.HomeModel.SceneName);

        /// <summary>Back to the main menu (stops a local match once the screen is covered).</summary>
        public void OpenHome()
        {
            if (!CanOpenHome) return;
            MenuOpen = false;
            LoadScene(Menu.HomeModel.SceneName, Simulation.Session.LocalSession.Stop, "HOME");
        }

        /// <summary>Starts a local training match on <paramref name="scene"/> (worlds start once the screen is covered).</summary>
        public bool StartTraining(string scene, string label = null) =>
            !string.IsNullOrEmpty(scene) && LoadScene(scene, () => Simulation.Session.LocalSession.Start(), label);

        /// <summary>The hangar scene is in the build, so a match can go back to it.</summary>
        public bool CanOpenHangar => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != HangarStage.SceneName &&
                                     Application.CanStreamedLevelBeLoaded(HangarStage.SceneName);

        /// <summary>Leaves the match (<see cref="Simulation.Session.LocalSession.Stop"/>) and loads the hangar.</summary>
        public void OpenHangar()
        {
            if (!CanOpenHangar) return;
            MenuOpen = false;
            // Stopped once the screen is covered, so the match does not vanish before the transition.
            LoadScene(HangarStage.SceneName, Simulation.Session.LocalSession.Stop);
        }

        /// <summary>A scene of the match pool or training list: the hangar is the only menu scene so far.</summary>
        public bool IsMatchScene => _musicSet == MusicLibrary.Set.Match;

        /// <summary>
        /// Every scene change goes through here: with the scene flow, the screen breaks up into blocks, then
        /// <paramref name="whenCovered"/> runs and the scene loads; without it (no root scope) both happen at once.
        /// False when a transition already runs.
        /// </summary>
        /// <param name="label">Shown on the transition; empty = the scene name.</param>
        public bool LoadScene(string scene, Action whenCovered = null, string label = null)
        {
            if (_flow != null && !_flow.IsDisposed)
            {
                if (_flow.Busy) return false;
                if (_flow.Load(scene, whenCovered, label)) return true;
            }
            whenCovered?.Invoke();
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
            return true;
        }

        /// <summary>Editor tuning: runs the transition with no scene change. False with no scene flow or while one runs.</summary>
        public bool PreviewTransition() => _flow != null && !_flow.IsDisposed && _flow.Preview();

        /// <summary>
        /// The game's scene flow (root scope; it outlives this scene): music and volumes move to it, and this scene tells
        /// it when it is ready to show. Called when the scene loads; music already started moves over.
        /// </summary>
        public void UseSceneFlow(SceneFlow flow)
        {
            if (flow == null || flow == _flow || flow.IsDisposed) return;
            _flow = flow;
            flow.Attach(Sounds);
            _audio.UseShared(flow.Music, flow.Volumes);
            if (_musicStarted) StartMusic();
        }

        /// <summary>
        /// Call from Start or later: the mixer ignores volume changes during Awake. Music: <paramref name="musicOverride"/>
        /// if set, else the config's <see cref="MusicLibrary"/> set (menu identity track, or the match playlist).
        /// </summary>
        public void Start(MusicLibrary.Set musicSet, AudioClip musicOverride)
        {
            _musicSet = musicSet;
            _musicOverride = musicOverride;
            _musicStarted = true;
            _audio.ApplySavedVolumes();
            StartMusic();
            StartAmbience();
        }

        /// <summary>City maps (<c>Map_*</c> scenes): the city's sound under everything, on the Ambient channel.</summary>
        private void StartAmbience()
        {
            if (_ambience != null || Sounds == null || !_scene.name.StartsWith("Map_", StringComparison.Ordinal)) return;
            _ambience = _audio.StartLoop(Sounds.CityAmbience, Vector3.zero);
        }

        private void StartMusic()
        {
            _musicLevel = -1f;
            var music = Config != null ? Config.Music : null;
            // Across a scene change the old scene's music is still playing: cross-fade into this scene's set.
            var fade = music != null && _flow != null ? music.SceneCrossFadeSeconds : 1.5f;
            if (_musicOverride != null) _audio.PlayMusic(_musicOverride, fade);
            else if (music == null) return;
            else if (_musicSet == MusicLibrary.Set.Menu) _audio.PlayMusic(music.Menu, fade);
            else _audio.PlayPlaylist(music.Match, music.CrossFadeSeconds);
            UpdateMusicLevel();
        }

        /// <summary>
        /// Menus: full level. Matches: down while you fly, so the rotors and guns come through; up a little while you
        /// wait to spawn and on the results.
        /// </summary>
        private void UpdateMusicLevel()
        {
            var music = Config != null ? Config.Music : null;
            if (music == null || !_musicStarted) return;
            var flying = Hud.HasChopper && !Match.BannerVisible;
            var level = _musicSet == MusicLibrary.Set.Menu ? music.MenuLevel : flying ? music.CombatLevel : music.IdleLevel;
            if (Mathf.Approximately(level, _musicLevel)) return;
            _musicLevel = level;
            _audio.SetMusicLevel(level, music.LevelFadeSeconds);
        }

        /// <summary>In a cockpit view, sounds are heard from inside (<see cref="CueMuffle"/>). Blends in log steps, as hearing does.</summary>
        private void UpdateOutsideSounds(float deltaTime)
        {
            var sounds = Sounds;
            if (sounds == null) return;
            var target = CameraRig != null && CameraRig.HidesOwnChopper ? 1f : 0f;
            _cockpitMuffle = Mathf.Lerp(_cockpitMuffle, target, 1f - Mathf.Exp(-sounds.CutoffSpeed * deltaTime));
            _audio.SetCutoffs(LogLerp(sounds.ChaseCutoff, sounds.CockpitCutoff, _cockpitMuffle),
                LogLerp(sounds.ChaseCutoff, sounds.CabinCutoff, _cockpitMuffle));
        }

        private static float LogLerp(float from, float to, float t) => Mathf.Exp(Mathf.Lerp(Mathf.Log(from), Mathf.Log(to), t));

        public void Tick(float deltaTime, float unscaledDeltaTime)
        {
            _audio.Tick(unscaledDeltaTime);
            UpdateMusicLevel();
            UpdateOutsideSounds(unscaledDeltaTime);
            // The scene flow keeps the screen covered until this scene is ready: a match scene once your chopper spawned.
            if (_flow != null && _flow.WaitingFor(_scene) && (!IsMatchScene || Hud.HasChopper)) _flow.SceneReady(_scene);
            Effects.Tick(deltaTime);
            ModuleVisuals.Tick(deltaTime);
            Hud.Tick(deltaTime);
            Prompts.Tick();
            Match.SetScoreboardHeld(!MenuOpen && _controls.Flight.Scoreboard.IsPressed());
            Match.Tick(deltaTime);
            Results.Tick(unscaledDeltaTime);
            HudLook.CockpitView = CameraRig != null && CameraRig.HidesOwnChopper;
            HudLook.Tick(unscaledDeltaTime);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            foreach (var owned in _owned) owned.Dispose();
            HudLook.Dispose();
            PilotPortrait.Dispose();
            Events.Dispose();
            Effects.Dispose();
            ModuleVisuals.Dispose();
            _audio.Dispose();
            _controls.Flight.Scoreboard.Disable();
            _controls.Dispose();
        }
    }
}
