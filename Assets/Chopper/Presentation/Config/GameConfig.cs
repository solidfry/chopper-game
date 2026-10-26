using System.Collections.Generic;
using Chopper.Presentation.Audio;
using Chopper.Presentation.Effects;
using Chopper.Simulation.Config;
using Chopper.Simulation.Loadout;
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

        [Header("Presentation")]
        [SerializeField] private SoundLibrary audio;
        [SerializeField] private CombatEffectsSettings effects;
        [SerializeField, Tooltip("Default music for gameplay scenes. A bridge can override it.")]
        private AudioClip matchMusic;

        [Header("UI")]
        [SerializeField, Tooltip("Shared panel settings (theme, scaling) for every UI Toolkit document.")]
        private PanelSettings panelSettings;
        [SerializeField] private VisualTreeAsset hudLayout;

        public LoadoutCatalog LoadoutCatalog => loadoutCatalog;
        public SoundLibrary Audio => audio;
        public CombatEffectsSettings Effects => effects;
        public AudioClip MatchMusic => matchMusic;
        public PanelSettings PanelSettings => panelSettings;
        public VisualTreeAsset HudLayout => hudLayout;

        public void CollectProblems(List<string> problems)
        {
            Require(loadoutCatalog, "loadout catalog", problems);
            Require(audio, "sound library", problems);
            Require(effects, "combat effects settings", problems);
            Require(panelSettings, "panel settings", problems);
            Require(hudLayout, "HUD layout", problems);

            if (loadoutCatalog != null) loadoutCatalog.CollectProblems(problems);
            if (audio != null) audio.CollectProblems(problems);
            if (effects != null) effects.CollectProblems(problems);
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(LoadoutCatalog catalog, SoundLibrary soundLibrary, CombatEffectsSettings effectsSettings, AudioClip music,
            PanelSettings panel, VisualTreeAsset hud)
        {
            loadoutCatalog = catalog;
            audio = soundLibrary;
            effects = effectsSettings;
            matchMusic = music;
            panelSettings = panel;
            hudLayout = hud;
        }

        private void Require(Object value, string label, List<string> problems)
        {
            if (value == null) problems.Add($"{name}: no {label} set.");
        }
    }
}
