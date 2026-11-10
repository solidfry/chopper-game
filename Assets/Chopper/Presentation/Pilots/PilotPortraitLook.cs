using System;
using UnityEngine;

namespace Chopper.Presentation.Pilots
{
    /// <summary>
    /// How the pilot portrait looks and moves (HUD and select screen). On <see cref="Hud.HudLookSettings"/>; read by
    /// <see cref="PilotPortraitRig"/>. The shader is set by Chopper → Setup, step 1.
    /// </summary>
    [Serializable]
    public sealed class PilotPortraitLook
    {
        [Tooltip("Chopper/PilotPortrait.")] public Shader Shader;
        [Tooltip("Render texture size in pixels (the art is 2:3).")] public Vector2Int Size = new(208, 312);

        [Header("Look")]
        [Tooltip("HUD colour of the portrait: the UI green (--chopper-green, #00ff94).")] public Color HudColour = new(0f, 1f, 0.58f);
        [Range(0f, 1f), Tooltip("HUD: how much of the colour goes to the HUD colour (a duotone). 0 = the scene's light colours only.")]
        public float Tint = 1f;
        [Range(0f, 1f), Tooltip("Select screen: how much of the colour goes to the pilot's accent.")] public float SelectTint = 0.35f;
        [Range(0f, 1f), Tooltip("How dark the gaps between scanlines are.")] public float Scanlines = 0.55f;
        [Tooltip("Scanlines from top to bottom (about a third of the picture's height in screen pixels keeps them clear).")]
        public float ScanlineCount = 52f;
        [Range(0f, 1f), Tooltip("How much the scene light changes the art. 0 = the art as painted.")] public float LightAmount = 1f;
        [Tooltip("Sun strength against the ambient light: higher = a harder light side and shadow side.")] public float SunBoost = 4f;
        [Tooltip("Light contrast (a power on the light): higher = darker shadows and brighter lit parts.")] public float LightContrast = 1.8f;
        [Tooltip("Scale on the sun and ambient light (the art is already shaded: keep it near 1).")] public float Exposure = 0.9f;
        [Tooltip("Accent glow from below: the cockpit screens.")] public float ScreenGlow = 0.25f;
        [Range(0f, 2f), Tooltip("Accent light on the edges of shapes.")] public float Rim = 0.35f;

        [Header("Motion")]
        [Tooltip("Seconds per breath (half as long while boosting).")] public float BreathSeconds = 4.2f;
        [Tooltip("Shortest and longest wait between blinks, seconds.")] public Vector2 BlinkWait = new(2f, 5.5f);
        [Tooltip("Idle head movement (1 = a lot).")] public float Idle = 0.3f;
        [Tooltip("Voice loudness (RMS) times this = a fully open jaw.")] public float TalkGain = 7f;
        [Tooltip("Head lean per degree of chopper roll (radians).")] public float RollLean = 0.0006f;
        [Tooltip("Head turn into a turn: 1 at this yaw rate (degrees per second).")] public float TurnLookRate = 360f;
        [Tooltip("Head shake on a hit that takes this part of the hull (more for bigger hits).")] public float HitShake = 0.007f;
    }
}
