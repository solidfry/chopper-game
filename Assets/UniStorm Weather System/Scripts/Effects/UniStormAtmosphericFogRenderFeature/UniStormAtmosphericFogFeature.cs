using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

public class UniStormAtmosphericFogFeature : ScriptableRendererFeature
{
    public enum DitheringControl { Enabled, Disabled }

    [System.Serializable]
    public class Settings
    {
        public bool isEnabled = true;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

        public Texture2D NoiseTexture = null;

        public DitheringControl Dither = DitheringControl.Enabled;
        [System.NonSerialized] public Light SunSource;
        [System.NonSerialized] public Light MoonSource;

        public bool distanceFog = true;
        public bool useRadialDistance = false;
        public bool heightFog = false;
        public float height = 1.0f;
        public float heightDensity = 2.0f;
        public float startDistance = 0.0f;

        public Color SunColor = new Color(1, 0.63529f, 0);
        public Color MoonColor = new Color(1, 0.63529f, 0);
        public Color TopColor;
        public Color BottomColor;
        public float BlendHeight = 0.03f;
        public float FogGradientHeight = 0.5f;
        public float SunIntensity = 2;
        public float MoonIntensity = 1;
        public float SunFalloffIntensity = 9.4f;
        public float SunControl = 1;
        public float MoonControl = 1;
    }

    public Settings settings = new Settings();
    private UniStormAtmosphericFogPass fogPass;

    Shader copyDepthShader = null;
    Material copyDepthPassMaterial = null;
    CopyDepthPass copyDepthPass;

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!settings.isEnabled || fogPass == null)
            return;

        fogPass.renderPassEvent = settings.renderPassEvent;
        fogPass.settings = settings;

        // The shader samples _CameraDepthTexture.
        fogPass.ConfigureInput(ScriptableRenderPassInput.Depth);

        renderer.EnqueuePass(fogPass);
    }

    public override void Create()
    {
        fogPass = new UniStormAtmosphericFogPass(name);

        if (Application.isEditor && !Application.isPlaying)
            return;

        copyDepthShader = Shader.Find("Hidden/Universal Render Pipeline/CopyDepth");
        if (copyDepthShader != null)
        {
            copyDepthPassMaterial = new Material(copyDepthShader);
            copyDepthPass = new CopyDepthPass(settings.renderPassEvent, copyDepthShader);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (copyDepthPassMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(copyDepthPassMaterial);
            else
                DestroyImmediate(copyDepthPassMaterial);
        }
    }
}