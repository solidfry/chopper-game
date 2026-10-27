using System;
using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Presentation.Hud
{
    /// <summary>How the HUD is drawn. Clean = flat UI on top of the game (no extra pass).</summary>
    public enum HudLookPreset
    {
        Clean = 0,
        Vector = 1,
        Crt84 = 2,
    }

    /// <summary>Base look of one preset. Values are read by the <c>Hidden/Chopper/HudLook</c> shader.</summary>
    [Serializable]
    public struct HudLookProfile
    {
        [Range(0f, 3f), Tooltip("Phosphor glow round the HUD lines. 0 = none.")]
        public float Glow;
        [Range(0f, 8f), Tooltip("Glow spread, in HUD texture pixels.")]
        public float GlowRadius;
        [Range(0f, 1f), Tooltip("How dark the gaps between scanlines are.")]
        public float Scanlines;
        [Tooltip("Scanlines from the top to the bottom of the screen.")]
        public float ScanlineCount;
        [Range(0f, 0.3f), Tooltip("Barrel distortion of the HUD (the game view is not bent).")]
        public float Curvature;
        [Range(0f, 6f), Tooltip("Colour split at the screen edge, in HUD texture pixels.")]
        public float Chromatic;
        [Range(0f, 1f), Tooltip("HUD gets darker towards the corners.")]
        public float Vignette;
        [Range(0f, 0.5f), Tooltip("Static noise on the HUD lines.")]
        public float Noise;
        [Range(0f, 0.5f), Tooltip("Base brightness flicker. Off when the player turns on 'reduce effects'.")]
        public float Flicker;
        [Range(0f, 1f), Tooltip("Phosphor trail: moving HUD parts leave a fading ghost. 0 = off (no extra pass).")]
        public float Persistence;
        [Range(0.01f, 1f), Tooltip("Seconds for the trail to fade to about a third.")]
        public float PersistenceTime;
    }

    /// <summary>How strongly the look reacts to gameplay (hits, low hull, locks, missiles). Same for every preset.</summary>
    [Serializable]
    public struct HudLookReactions
    {
        [Tooltip("Damage in one hit that gives a full glitch.")]
        public float FullGlitchDamage;
        [Tooltip("Glitch lost per second (1 = a full glitch clears in 1 s).")]
        public float GlitchDecay;
        [Tooltip("Extra colour split at full glitch, HUD texture pixels.")]
        public float GlitchChromatic;
        [Range(0f, 0.6f), Tooltip("Extra flicker while the hull is low.")]
        public float LowHullFlicker;
        [Range(0f, 1f), Tooltip("Brightness and glow pulse when your lock completes.")]
        public float LockPulse;
        [Tooltip("Pulse lost per second.")]
        public float PulseDecay;
        [Range(0f, 0.5f), Tooltip("Extra noise when a missile is about to hit.")]
        public float MissileNoise;
        [Range(0f, 1f), Tooltip("Glitch when the chopper dies or spawns (the HUD drops out / boots up).")]
        public float SpawnGlitch;
    }

    /// <summary>
    /// Retro 80s HUD look (plan section 3b): the HUD panel renders into a texture, and
    /// <see cref="HudLookRendererFeature"/> draws it over the game with glow, scanlines, curvature and glitches.
    /// Hangs off <see cref="Config.GameConfig"/>; made by Chopper → Setup.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/HUD/HUD Look Settings", fileName = "HudLookSettings")]
    public sealed class HudLookSettings : ScriptableObject, IValidatedAsset
    {
        [SerializeField, Tooltip("Preset until the player picks one in the options.")]
        private HudLookPreset defaultPreset = HudLookPreset.Crt84;
        [SerializeField, Range(0.25f, 1f), Tooltip("HUD texture size as a part of the screen size. Lower = softer and cheaper.")]
        private float renderScale = 1f;

        [SerializeField] private HudLookProfile vector = new()
        {
            Glow = 1f, GlowRadius = 2f, Scanlines = 0f, ScanlineCount = 540f, Curvature = 0f,
            Chromatic = 0.5f, Vignette = 0f, Noise = 0.02f, Flicker = 0f, Persistence = 0.35f, PersistenceTime = 0.08f,
        };

        [SerializeField] private HudLookProfile crt84 = new()
        {
            Glow = 1.4f, GlowRadius = 3f, Scanlines = 0.35f, ScanlineCount = 405f, Curvature = 0.06f,
            Chromatic = 1.5f, Vignette = 0.35f, Noise = 0.06f, Flicker = 0.04f, Persistence = 0.5f, PersistenceTime = 0.12f,
        };

        [SerializeField] private HudLookReactions reactions = new()
        {
            FullGlitchDamage = 40f, GlitchDecay = 2.5f, GlitchChromatic = 4f, LowHullFlicker = 0.2f,
            LockPulse = 0.5f, PulseDecay = 3f, MissileNoise = 0.15f, SpawnGlitch = 0.8f,
        };

        public HudLookPreset DefaultPreset => defaultPreset;
        public float RenderScale => renderScale;
        public HudLookReactions Reactions => reactions;

        /// <summary>False for <see cref="HudLookPreset.Clean"/>: the HUD is then drawn as plain UI.</summary>
        public bool TryGetProfile(HudLookPreset preset, out HudLookProfile profile)
        {
            switch (preset)
            {
                case HudLookPreset.Vector: profile = vector; return true;
                case HudLookPreset.Crt84: profile = crt84; return true;
                default: profile = default; return false;
            }
        }

        public void CollectProblems(List<string> problems)
        {
            if (renderScale <= 0f) problems.Add($"{name}: render scale must be above 0.");
            if (reactions.FullGlitchDamage <= 0f) problems.Add($"{name}: full glitch damage must be above 0.");
            if (reactions.GlitchDecay <= 0f || reactions.PulseDecay <= 0f)
                problems.Add($"{name}: glitch and pulse decay must be above 0, or the effect never clears.");
            if (vector.ScanlineCount <= 0f || crt84.ScanlineCount <= 0f) problems.Add($"{name}: scanline count must be above 0.");
        }
    }
}
