using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class UniStormCloudShadowsFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public bool isEnabled = true;
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        public float Fade = 1f;
        public RenderTexture CloudShadowTexture;
        public Color ShadowColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        public float CloudTextureScale = 0.1f;
        public float BottomThreshold = 0f;
        public float TopThreshold = 1f;
        public float ShadowIntensity = 1f;
        public Material ScreenSpaceShadowsMaterial;
        public Vector3 ShadowDirection;

        public float m_CurrentCloudHeight;
        public int CloudSpeed;

        public float NormalY = 1f;

        [Header("Stable Sun Follow")]
        [Tooltip("UV offset amount added from the sun direction. This keeps the shadows loosely following the sun without physical projection flipping.")]
        public float SunProjectionOffset = 0.25f;

        [Tooltip("How quickly the sun offset catches up when the sun moves. Higher = snappier, lower = smoother.")]
        public float SunProjectionSmoothing = 8f;

        [Tooltip("Minimum amount of sun offset even when the sun is high overhead.")]
        [Range(0f, 1f)] public float MinimumSunOffset = 0.15f;
    }

    public Settings settings = new Settings();

    UniStormCloudShadowsPass cloudShadowsPass;

    public override void Create()
    {
        cloudShadowsPass = new UniStormCloudShadowsPass(name);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!settings.isEnabled)
            return;

        cloudShadowsPass.renderPassEvent = settings.renderPassEvent;
        cloudShadowsPass.settings = settings;
        renderer.EnqueuePass(cloudShadowsPass);
    }

    protected override void Dispose(bool disposing)
    {
        cloudShadowsPass?.Dispose();
    }
}
