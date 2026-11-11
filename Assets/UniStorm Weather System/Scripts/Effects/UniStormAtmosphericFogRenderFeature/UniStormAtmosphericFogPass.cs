using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

public class UniStormAtmosphericFogPass : ScriptableRenderPass
{
    public UniStormAtmosphericFogFeature.Settings settings;

    private readonly string m_ProfilerTag;
    private readonly Material fogMaterial;

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int CameraWSId = Shader.PropertyToID("_CameraWS");
    private static readonly int HeightParamsId = Shader.PropertyToID("_HeightParams");
    private static readonly int DistanceParamsId = Shader.PropertyToID("_DistanceParams");
    private static readonly int SunVectorId = Shader.PropertyToID("_SunVector");
    private static readonly int MoonVectorId = Shader.PropertyToID("_MoonVector");
    private static readonly int SunIntensityId = Shader.PropertyToID("_SunIntensity");
    private static readonly int MoonIntensityId = Shader.PropertyToID("_MoonIntensity");
    private static readonly int SunAlphaId = Shader.PropertyToID("_SunAlpha");
    private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
    private static readonly int MoonColorId = Shader.PropertyToID("_MoonColor");
    private static readonly int UpperColorId = Shader.PropertyToID("_UpperColor");
    private static readonly int BottomColorId = Shader.PropertyToID("_BottomColor");
    private static readonly int FogBlendHeightId = Shader.PropertyToID("_FogBlendHeight");
    private static readonly int FogGradientHeightId = Shader.PropertyToID("_FogGradientHeight");
    private static readonly int SunControlId = Shader.PropertyToID("_SunControl");
    private static readonly int MoonControlId = Shader.PropertyToID("_MoonControl");
    private static readonly int EnableDitheringId = Shader.PropertyToID("_EnableDithering");
    private static readonly int NoiseTexId = Shader.PropertyToID("_NoiseTex");
    private static readonly int SceneFogParamsId = Shader.PropertyToID("_SceneFogParams");
    private static readonly int SceneFogModeId = Shader.PropertyToID("_SceneFogMode");
    private static readonly int InvViewProjId = Shader.PropertyToID("_InvViewProj");
    private static readonly int VRSinglePassEnabledId = Shader.PropertyToID("_VRSinglePassEnabled");

    public UniStormAtmosphericFogPass(string tag)
    {
        m_ProfilerTag = tag;

        Shader shader = Shader.Find("UniStorm/URP/UniStormAtmosphericFog");
#if UNITY_EDITOR
        if (shader == null)
            return;
#endif
        fogMaterial = new Material(shader);
    }

    private void SetupDirectionalLights()
    {
        settings.SunSource = null;
        settings.MoonSource = null;

        Light[] directionalLights = Light.GetLights(LightType.Directional, 0);

        foreach (Light directionalLight in directionalLights)
        {
            if (directionalLight == null)
                continue;

            string n = directionalLight.gameObject.name;

            if (settings.SunSource == null &&
                n.IndexOf("sun", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                settings.SunSource = directionalLight;
                continue;
            }

            if (settings.MoonSource == null &&
                n.IndexOf("moon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                settings.MoonSource = directionalLight;
            }
        }
    }

    private class PassData
    {
        public TextureHandle source;
        public TextureHandle temp;
        public Material fogMaterial;
        public int passIndex;
        public bool stereoInstancing;
        public bool stereoMultiview;
    }

    private void UpdateMaterial(Camera camera, bool stereoInstancing, bool stereoMultiview)
    {
        Transform camTr = camera.transform;
        Vector3 camPos = camTr.position;

        float fDotC = camPos.y - settings.height;
        float paramK = fDotC <= 0.0f ? 1.0f : 0.0f;

        Matrix4x4 view = camera.worldToCameraMatrix;
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
        Matrix4x4 invViewProj = (proj * view).inverse;

        fogMaterial.SetMatrix(InvViewProjId, invViewProj);
        fogMaterial.SetVector(CameraWSId, new Vector4(camPos.x, camPos.y, camPos.z, 1.0f));
        fogMaterial.SetVector(HeightParamsId, new Vector4(settings.height, fDotC, paramK, settings.heightDensity * 0.5f));
        fogMaterial.SetVector(DistanceParamsId, new Vector4(-Mathf.Max(settings.startDistance, 0.0f), 0f, 0f, 0f));

        if (settings.SunSource != null)
            fogMaterial.SetVector(SunVectorId, -settings.SunSource.transform.forward.normalized);

        if (settings.MoonSource != null)
            fogMaterial.SetVector(MoonVectorId, -settings.MoonSource.transform.forward.normalized);

        fogMaterial.SetFloat(SunIntensityId, settings.SunIntensity);
        fogMaterial.SetFloat(MoonIntensityId, settings.MoonIntensity);
        fogMaterial.SetFloat(SunAlphaId, settings.SunFalloffIntensity);
        fogMaterial.SetColor(SunColorId, settings.SunColor);
        fogMaterial.SetColor(MoonColorId, settings.MoonColor);
        fogMaterial.SetColor(UpperColorId, settings.TopColor);
        fogMaterial.SetColor(BottomColorId, settings.BottomColor);
        fogMaterial.SetFloat(FogBlendHeightId, settings.BlendHeight);
        fogMaterial.SetFloat(FogGradientHeightId, settings.FogGradientHeight);
        fogMaterial.SetFloat(SunControlId, settings.SunControl);
        fogMaterial.SetFloat(MoonControlId, settings.MoonControl);
        fogMaterial.SetFloat(VRSinglePassEnabledId, stereoInstancing || stereoMultiview ? 1f : 0f);

        if (settings.Dither == UniStormAtmosphericFogFeature.DitheringControl.Enabled)
        {
            fogMaterial.SetFloat(EnableDitheringId, 1f);
            fogMaterial.SetTexture(NoiseTexId, settings.NoiseTexture);
        }
        else
        {
            fogMaterial.SetFloat(EnableDitheringId, 0f);
        }

        FogMode sceneMode = RenderSettings.fogMode;
        float sceneDensity = RenderSettings.fogDensity;
        float sceneStart = RenderSettings.fogStartDistance;
        float sceneEnd = RenderSettings.fogEndDistance;

        Vector4 sceneParams;
        bool linear = sceneMode == FogMode.Linear;
        float diff = linear ? sceneEnd - sceneStart : 0.0f;
        float invDiff = Mathf.Abs(diff) > 0.0001f ? 1.0f / diff : 0.0f;

        sceneParams.x = sceneDensity * 1.2011224087f;
        sceneParams.y = sceneDensity * 1.4426950408f;
        sceneParams.z = linear ? -invDiff : 0.0f;
        sceneParams.w = linear ? sceneEnd * invDiff : 0.0f;

        fogMaterial.SetVector(SceneFogParamsId, sceneParams);
        fogMaterial.SetVector(SceneFogModeId, new Vector4((int)sceneMode, settings.useRadialDistance ? 1f : 0f, 0f, 0f));
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
#if UNITY_EDITOR
        if (fogMaterial == null || UnityEditor.EditorApplication.isPaused)
            return;
#endif
        if (!Application.isPlaying)
            return;

        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        Camera camera = cameraData.camera;

        if (cameraData.cameraType == CameraType.SceneView)
            return;

        if (resourceData.isActiveTargetBackBuffer)
            return;

        SetupDirectionalLights();

        RenderTextureDescriptor cameraDesc = cameraData.cameraTargetDescriptor;
        bool stereoInstancing = false;
        bool stereoMultiview = false;

        if (XRSettings.enabled && XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.SinglePassInstanced)
        {
            if (cameraDesc.volumeDepth == 2)
                stereoInstancing = true;
        }

        if (XRSettings.enabled &&
            XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.MultiPass &&
            camera.stereoActiveEye != Camera.MonoOrStereoscopicEye.Mono &&
            camera.stereoEnabled)
        {
            stereoMultiview = true;
        }

        UpdateMaterial(camera, stereoInstancing, stereoMultiview);

        int passIndex = 0;
        if (settings.distanceFog && settings.heightFog)
            passIndex = 0;
        else if (settings.distanceFog)
            passIndex = 1;
        else
            passIndex = 2;

        TextureHandle source = resourceData.activeColorTexture;

        RenderTextureDescriptor tempDesc = cameraDesc;
        tempDesc.depthBufferBits = 0;
        TextureHandle temp = UniversalRenderer.CreateRenderGraphTexture(renderGraph, tempDesc, "_UniStormFogTemp", false);

        using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData))
        {
            passData.source = source;
            passData.temp = temp;
            passData.fogMaterial = fogMaterial;
            passData.passIndex = passIndex;
            passData.stereoInstancing = stereoInstancing;
            passData.stereoMultiview = stereoMultiview;

            builder.UseTexture(source, AccessFlags.ReadWrite);
            builder.UseTexture(temp, AccessFlags.ReadWrite);

            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
            {
                CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                if (data.stereoInstancing)
                    cmd.EnableShaderKeyword("STEREO_INSTANCING_ON");

                if (data.stereoMultiview)
                    cmd.EnableShaderKeyword("STEREO_MULTIVIEW_ON");

                RTHandle sourceRT = data.source;
                RTHandle tempRT = data.temp;

                cmd.SetGlobalTexture(MainTexId, sourceRT.nameID);
                cmd.Blit(sourceRT.nameID, tempRT.nameID, data.fogMaterial, data.passIndex);
                cmd.Blit(tempRT.nameID, sourceRT.nameID);

                if (data.stereoInstancing)
                    cmd.DisableShaderKeyword("STEREO_INSTANCING_ON");

                if (data.stereoMultiview)
                    cmd.DisableShaderKeyword("STEREO_MULTIVIEW_ON");
            });
        }
    }
}