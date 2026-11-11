using System.Collections.Generic;
using Chopper.Presentation.Audio;
using Chopper.Presentation.Camera;
using Chopper.Presentation.Effects;
using Chopper.Presentation.Hangar;
using Chopper.Presentation.Hud;
using Chopper.Presentation.Menu;
using Chopper.Presentation.Visuals;
using Chopper.Simulation.Config;
using Chopper.Simulation.Loadout;
using Chopper.Simulation.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.Presentation.Config
{
    /// <summary>
    /// Config root: points to every system's settings asset. The scene's <see cref="Bridge.PresentationBridge"/> reads
    /// it; the Chopper Setup window creates, repairs and validates it. Add new systems' settings here, not elsewhere.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject, IValidatedAsset
    {
        [Header("Content")]
        [SerializeField] private LoadoutCatalog loadoutCatalog;
        [SerializeField, Tooltip("Paint skins of the chopper kit (PartForge). Setup step 5 links it.")]
        private SkinLibrary skins;

        [Header("Presentation")]
        [SerializeField] private SoundLibrary audio;
        [SerializeField] private CombatEffectsSettings effects;
        [SerializeField, Tooltip("Chopper camera: views (chase, cockpit), blends, speed effects, shake.")]
        private CameraSettings camera;
        [SerializeField, Tooltip("Identity track (hangar, menus) and the match playlist. Each scene's bridge picks the set.")]
        private MusicLibrary music;

        [Header("UI")]
        [SerializeField, Tooltip("Shared panel settings (theme, scaling) for every UI Toolkit document.")]
        private PanelSettings panelSettings;
        [SerializeField] private VisualTreeAsset hudLayout;
        [SerializeField, Tooltip("In-game options overlay (Escape).")] private VisualTreeAsset optionsLayout;
        [SerializeField, Tooltip("End-of-match screen (stats, awards, XP). Setup links it.")] private VisualTreeAsset resultsLayout;
        [SerializeField, Tooltip("Retro HUD look: presets, intensities, gameplay reactions. Empty = plain UI HUD.")]
        private HudLookSettings hudLook;
        [SerializeField, Tooltip("Hangar screen (loadout and skin). Setup step 7 links it.")] private VisualTreeAsset hangarLayout;
        [SerializeField, Tooltip("Home screen (main menu, pilot codex). Setup step 11 links it.")] private VisualTreeAsset homeLayout;
        [SerializeField, Tooltip("Live cockpit screens: the pages drawn on the cockpit displays.")]
        private VisualTreeAsset cockpitScreensLayout;
        [SerializeField, Tooltip("Draws the cockpit screen pages into the render texture the kit's screen material reads.")]
        private PanelSettings cockpitScreensPanel;

        [Header("Hangar")]
        [SerializeField, Tooltip("Hangar wireframe look and camera. Setup step 7 links it.")] private HangarSettings hangar;
        [SerializeField, Tooltip("Look of each time of day and weather (city match scenes). Setup step 10 links it.")]
        private EnvironmentSettings environment;
        [SerializeField, Tooltip("Every playable map (training list, match pool). Setup step 10 links it.")]
        private MapCatalog maps;
        [SerializeField, Tooltip("Game modes on the PLAY page and their matchmaker queues. Setup step 11 links it.")]
        private GameModeCatalog modes;

        public LoadoutCatalog LoadoutCatalog => loadoutCatalog;
        public SkinLibrary Skins => skins;
        public SoundLibrary Audio => audio;
        public CombatEffectsSettings Effects => effects;
        public CameraSettings Camera => camera;
        public MusicLibrary Music => music;
        public PanelSettings PanelSettings => panelSettings;
        public VisualTreeAsset HudLayout => hudLayout;
        public VisualTreeAsset OptionsLayout => optionsLayout;
        public VisualTreeAsset ResultsLayout => resultsLayout;
        public HudLookSettings HudLook => hudLook;
        public VisualTreeAsset HangarLayout => hangarLayout;
        public VisualTreeAsset HomeLayout => homeLayout;
        public VisualTreeAsset CockpitScreensLayout => cockpitScreensLayout;
        public PanelSettings CockpitScreensPanel => cockpitScreensPanel;
        public HangarSettings Hangar => hangar;
        public EnvironmentSettings Environment => environment;
        public MapCatalog Maps => maps;
        public GameModeCatalog Modes => modes;

        public void CollectProblems(List<string> problems)
        {
            Require(loadoutCatalog, "loadout catalog", problems);
            Require(audio, "sound library", problems);
            Require(music, "music library", problems);
            Require(effects, "combat effects settings", problems);
            Require(camera, "camera settings", problems);
            Require(panelSettings, "panel settings", problems);
            Require(hudLayout, "HUD layout", problems);
            Require(optionsLayout, "options layout", problems);
            Require(resultsLayout, "match results layout", problems);
            Require(hudLook, "HUD look settings", problems);
            Require(cockpitScreensLayout, "cockpit screens layout", problems);
            Require(cockpitScreensPanel, "cockpit screens panel settings", problems);
            Require(skins, "skin library (press '5. Import part kits')", problems);
            Require(hangar, "hangar settings (press '7. Build hangar')", problems);
            Require(hangarLayout, "hangar layout (press '7. Build hangar')", problems);
            Require(homeLayout, "home layout (press '11. Build home screen')", problems);
            Require(modes, "game mode catalog (press '11. Build home screen')", problems);

            if (loadoutCatalog != null) loadoutCatalog.CollectProblems(problems);
            if (audio != null) audio.CollectProblems(problems);
            if (music != null) music.CollectProblems(problems);
            if (effects != null) effects.CollectProblems(problems);
            if (camera != null) camera.CollectProblems(problems);
            if (hudLook != null) hudLook.CollectProblems(problems);
            if (skins != null) skins.CollectProblems(problems);
            if (hangar != null) hangar.CollectProblems(problems);
            if (environment != null) environment.CollectProblems(problems);
            if (maps != null) maps.CollectProblems(problems);
            if (modes != null) modes.CollectProblems(problems);
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(LoadoutCatalog catalog, SoundLibrary soundLibrary, CombatEffectsSettings effectsSettings, MusicLibrary music,
            PanelSettings panel, VisualTreeAsset hud, HudLookSettings look, VisualTreeAsset options, CameraSettings cameraSettings)
        {
            loadoutCatalog = catalog;
            audio = soundLibrary;
            effects = effectsSettings;
            this.music = music;
            panelSettings = panel;
            hudLayout = hud;
            hudLook = look;
            optionsLayout = options;
            camera = cameraSettings;
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetCockpitScreens(VisualTreeAsset layout, PanelSettings panel)
        {
            cockpitScreensLayout = layout;
            cockpitScreensPanel = panel;
        }

        /// <summary>Editor only: used by the kit import step.</summary>
        public void SetSkins(SkinLibrary library) => skins = library;

        /// <summary>Editor only: used by the hangar step.</summary>
        public void SetHangar(HangarSettings settings, VisualTreeAsset layout)
        {
            hangar = settings;
            hangarLayout = layout;
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetResults(VisualTreeAsset layout) => resultsLayout = layout;

        /// <summary>Editor only: used by the home screen step.</summary>
        public void SetHome(VisualTreeAsset layout) => homeLayout = layout;

        /// <summary>Editor only: used by the home screen step.</summary>
        public void SetModes(GameModeCatalog catalog) => modes = catalog;

        /// <summary>Editor only: used by the city match scene step.</summary>
        public void SetEnvironment(EnvironmentSettings settings) => environment = settings;

        /// <summary>Editor only: used by the city match scene step.</summary>
        public void SetMaps(MapCatalog catalog) => maps = catalog;

        private void Require(Object value, string label, List<string> problems)
        {
            if (value == null) problems.Add($"{name}: no {label} set.");
        }
    }
}
