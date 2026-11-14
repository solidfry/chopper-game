using System;
using Chopper.Simulation.Combat;
using UnityEngine;

namespace Chopper.Presentation.Camera
{
    /// <summary>
    /// How one special view looks: the screen grade (exposure, contrast, a channel mix that makes the picture one
    /// colour, grain, vignette) and the glow on marked targets. Tune on <see cref="CameraSettings"/>; a mode with no
    /// entry there uses <see cref="Default"/>.
    /// </summary>
    [Serializable]
    public sealed class VisionLook
    {
        [SerializeField] private VisionMode mode;
        [Header("Screen")]
        [SerializeField, Tooltip("Exposure change in stops. Night vision: above 0 (it sees in the dark).")] private float exposure;
        [SerializeField, Range(-100f, 100f)] private float contrast;
        [SerializeField, Tooltip("Red output = this mix of the red, green and blue input (1 = 100%).")] private Vector3 redMix = new(1f, 0f, 0f);
        [SerializeField] private Vector3 greenMix = new(0f, 1f, 0f);
        [SerializeField] private Vector3 blueMix = new(0f, 0f, 1f);
        [SerializeField, Range(0f, 1f)] private float grain;
        [SerializeField, Range(0f, 1f)] private float vignette;
        [Header("Target glow")]
        [SerializeField, ColorUsage(false, true), Tooltip("Glow at full strength (HDR: above 1 blooms).")] private Color glowColor = Color.clear;
        [SerializeField, Min(0f), Tooltip("Glow size near, metres.")] private float glowSize = 7f;
        [SerializeField, Min(0f), Tooltip("Smallest glow size as a part of the distance, so a far target stays a visible dot.")]
        private float glowAngularSize = 0.012f;
        [SerializeField, Tooltip("On: the glow shows through walls and terrain.")] private bool throughWalls;

        public VisionMode Mode => mode;
        public float Exposure => exposure;
        public float Contrast => contrast;
        public Vector3 RedMix => redMix;
        public Vector3 GreenMix => greenMix;
        public Vector3 BlueMix => blueMix;
        public float Grain => grain;
        public float Vignette => vignette;
        public Color GlowColor => glowColor;
        public float GlowSize => glowSize;
        public float GlowAngularSize => glowAngularSize;
        public bool ThroughWalls => throughWalls;

        /// <summary>Glow size for a target at <paramref name="distance"/>, metres.</summary>
        public float GlowSizeAt(float distance) => Mathf.Max(glowSize, distance * glowAngularSize);

        /// <summary>The built-in look of a mode (first tuning, written without the editor).</summary>
        public static VisionLook Default(VisionMode mode) => mode switch
        {
            // Bright green from all light, heavy grain: sees in the dark, washes out in daylight.
            VisionMode.Night => new VisionLook
            {
                mode = mode, exposure = 2.2f, contrast = 10f,
                redMix = new Vector3(0.10f, 0.20f, 0.05f), greenMix = new Vector3(0.40f, 0.90f, 0.20f), blueMix = new Vector3(0.10f, 0.25f, 0.05f),
                grain = 0.6f, vignette = 0.45f,
            },
            // A dark, cool grey world; hot things are white.
            VisionMode.Thermal => new VisionLook
            {
                mode = mode, exposure = -0.6f, contrast = 35f,
                redMix = new Vector3(0.28f, 0.55f, 0.10f), greenMix = new Vector3(0.30f, 0.59f, 0.11f), blueMix = new Vector3(0.33f, 0.65f, 0.13f),
                grain = 0.25f, vignette = 0.3f,
                glowColor = new Color(4f, 3.6f, 3f), glowSize = 7f,
            },
            // A dim violet world; what moves is magenta.
            VisionMode.Motion => new VisionLook
            {
                mode = mode, exposure = -1.2f, contrast = 20f,
                redMix = new Vector3(0.25f, 0.35f, 0.10f), greenMix = new Vector3(0.05f, 0.10f, 0.02f), blueMix = new Vector3(0.30f, 0.50f, 0.15f),
                grain = 0.35f, vignette = 0.4f,
                glowColor = new Color(4f, 0.6f, 3.5f), glowSize = 8f,
            },
            // Very dark blue and noisy; targets are amber, also behind walls.
            VisionMode.DeepScan => new VisionLook
            {
                mode = mode, exposure = -1.8f, contrast = 40f,
                redMix = new Vector3(0.05f, 0.10f, 0.02f), greenMix = new Vector3(0.20f, 0.40f, 0.08f), blueMix = new Vector3(0.30f, 0.60f, 0.12f),
                grain = 0.8f, vignette = 0.55f,
                glowColor = new Color(4f, 2.4f, 0.4f), glowSize = 6f, throughWalls = true,
            },
            _ => new VisionLook { mode = mode },
        };
    }

    /// <summary>A target the special view marks this frame.</summary>
    public struct VisionGlow
    {
        public Vector3 Position;
        /// <summary>0..1 (<see cref="VisionMath.Signal"/>).</summary>
        public float Strength;
    }
}
