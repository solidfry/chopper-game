using System.Collections.Generic;
using Chopper.Presentation.Audio;
using Chopper.Simulation.Config;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.Presentation.Flow
{
    /// <summary>
    /// Scene changes (owner, 7 Nov): the screen breaks up into glowing blocks ("digitise"), the next scene loads behind
    /// it, then the blocks clear. World sounds fade with it; music cross-fades from one scene's set into the next.
    /// On the root scope prefab (<c>App/RootLifetimeScope.prefab</c>, Setup step 1 links it), not on the GameConfig:
    /// the scene flow lives across scenes, before any scene's bridge.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/UI/Scene Transition Settings", fileName = "SceneTransitionSettings")]
    public sealed class SceneTransitionSettings : ScriptableObject, IValidatedAsset
    {
        [Header("UI")]
        [SerializeField, Tooltip("Own panel settings, drawn over every other panel (high sort order).")]
        private PanelSettings panelSettings;
        [SerializeField, Tooltip("UI/Transitions/SceneTransition.uxml.")] private VisualTreeAsset layout;

        [Header("Timing (seconds, unscaled)")]
        [SerializeField, Min(0.05f), Tooltip("The blocks cover the screen.")] private float coverSeconds = 0.55f;
        [SerializeField, Min(0.05f), Tooltip("The blocks clear from the new scene.")] private float revealSeconds = 0.75f;
        [SerializeField, Min(0f), Tooltip("Shortest time fully covered, so a fast load still reads as a transition.")]
        private float minHoldSeconds = 0.25f;
        [SerializeField, Min(1f), Tooltip("Match scenes stay covered until your chopper spawns, but never longer than this.")]
        private float maxHoldSeconds = 12f;
        [SerializeField, Tooltip("The first scene of the game also clears in from blocks.")] private bool revealOnBoot = true;

        [Header("Look")]
        [SerializeField, Range(8, 96), Tooltip("Block size in pixels at 1080p.")] private int blockSize = 30;
        [SerializeField, Range(0.02f, 0.5f), Tooltip("Width of the glowing front as a share of the transition.")]
        private float edgeWidth = 0.14f;
        [SerializeField, Range(0f, 1f), Tooltip("How much the front follows a sweep (left to right) and not only noise.")]
        private float sweep = 0.45f;
        [SerializeField, Range(0f, 360f), Tooltip("Direction of the sweep: 0 = left to right, 90 = top to bottom, 180 = right to left.")]
        private float sweepAngle;
        [SerializeField, Min(0f), Tooltip("How many times a second the front's colours change.")]
        private float frontFlickerRate = 24f;
        [SerializeField, Range(0, 16), Tooltip("Glitch bars. 0 = none.")] private int glitchBars = 4;
        [SerializeField, Range(0f, 1f), Tooltip("Glitch bar strength in the middle of the transition.")]
        private float glitchStrength = 0.55f;
        [SerializeField, Range(0f, 0.3f), Tooltip("Scan lines while fully covered. 0 = none.")]
        private float scanLines = 0.035f;
        [SerializeField] private Color background = new(0.012f, 0.035f, 0.02f, 1f);
        [SerializeField, Tooltip("Glowing front blocks.")] private Color edge = new(0f, 1f, 0.58f, 1f);
        [SerializeField, Tooltip("Some front blocks flash this colour.")] private Color accent = new(0f, 0.94f, 1f, 1f);
        [SerializeField, Tooltip("Glitch bars.")] private Color glitch = new(1f, 0.26f, 0.54f, 1f);

        [Header("Sound (2D, on the scene flow; plays across the scene change)")]
        [SerializeField] private SoundCue coverSound;
        [SerializeField] private SoundCue revealSound;

        public PanelSettings PanelSettings => panelSettings;
        public VisualTreeAsset Layout => layout;
        public float CoverSeconds => coverSeconds;
        public float RevealSeconds => revealSeconds;
        public float MinHoldSeconds => minHoldSeconds;
        public float MaxHoldSeconds => maxHoldSeconds;
        public bool RevealOnBoot => revealOnBoot;
        public int BlockSize => blockSize;
        public float EdgeWidth => edgeWidth;
        public float Sweep => sweep;
        public float SweepAngle => sweepAngle;
        public float FrontFlickerRate => frontFlickerRate;
        public int GlitchBars => glitchBars;
        public float GlitchStrength => glitchStrength;
        public float ScanLines => scanLines;
        public Color Background => background;
        public Color Edge => edge;
        public Color Accent => accent;
        public Color Glitch => glitch;
        public SoundCue CoverSound => coverSound;
        public SoundCue RevealSound => revealSound;

        public void CollectProblems(List<string> problems)
        {
            if (panelSettings == null) problems.Add($"{name}: no panel settings (scene changes cut without the block effect).");
            if (layout == null) problems.Add($"{name}: no layout (UI/Transitions/SceneTransition.uxml).");
            if (coverSound != null) coverSound.CollectProblems(problems);
            if (revealSound != null) revealSound.CollectProblems(problems);
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(PanelSettings panel, VisualTreeAsset tree, SoundCue cover, SoundCue reveal)
        {
            panelSettings = panel;
            layout = tree;
            coverSound = cover;
            revealSound = reveal;
        }
    }
}
