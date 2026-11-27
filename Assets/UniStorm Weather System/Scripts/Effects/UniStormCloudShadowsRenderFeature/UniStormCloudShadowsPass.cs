using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class UniStormCloudShadowsPass : ScriptableRenderPass
{
    public UniStormCloudShadowsFeature.Settings settings;

    Material screenSpaceShadowsMaterial;
    readonly string m_ProfilerTag;

    static readonly int MainTexID = Shader.PropertyToID("_MainTex");
    static readonly int CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture");
    static readonly int SunCloudOffsetID = Shader.PropertyToID("_SunCloudOffset");

    Vector2 smoothedSunCloudOffset;
    Vector2 lastValidSunXZ = Vector2.right;
    bool hasInitializedSunOffset;

    public UniStormCloudShadowsPass(string tag)
    {
        m_ProfilerTag = tag;

        ConfigureInput(ScriptableRenderPassInput.Depth);

        Shader unistormCloudShadowsShader = Shader.Find("UniStorm/URP/UniStormCloudShadows");
        if (unistormCloudShadowsShader == null)
        {
            Debug.LogError("UniStorm Cloud Shadows shader was not found: UniStorm/URP/UniStormCloudShadows");
            return;
        }

        screenSpaceShadowsMaterial = CoreUtils.CreateEngineMaterial(unistormCloudShadowsShader);
    }

    Vector2 GetStableSunCloudOffset()
    {
        if (settings == null)
            return smoothedSunCloudOffset;

        Vector3 sun = settings.ShadowDirection;

        if (sun.sqrMagnitude > 0.000001f)
            sun.Normalize();
        else
            sun = Vector3.down;

        Vector2 sunXZ = new Vector2(sun.x, sun.z);
        float sunXZMagnitude = sunXZ.magnitude;

        if (sunXZMagnitude > 0.001f)
        {
            lastValidSunXZ = sunXZ / sunXZMagnitude;
        }

        float verticalAmount = Mathf.Clamp01(Mathf.Abs(sun.y));
        float horizonAmount = 1f - verticalAmount;
        float offsetStrength = settings.SunProjectionOffset * Mathf.Lerp(Mathf.Clamp01(settings.MinimumSunOffset), 1f, horizonAmount);

        Vector2 targetOffset = lastValidSunXZ * offsetStrength;

        if (!hasInitializedSunOffset)
        {
            smoothedSunCloudOffset = targetOffset;
            hasInitializedSunOffset = true;
            return smoothedSunCloudOffset;
        }

        float smoothing = Mathf.Max(0f, settings.SunProjectionSmoothing);
        float t = smoothing <= 0f ? 1f : 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        smoothedSunCloudOffset = Vector2.Lerp(smoothedSunCloudOffset, targetOffset, t);

        return smoothedSunCloudOffset;
    }

    private class PassData
    {
        public TextureHandle source;
        public TextureHandle destination;
        public TextureHandle depth;
        public Material material;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
#if UNITY_EDITOR
        if (UnityEditor.EditorApplication.isPaused)
            return;
#endif
        if (screenSpaceShadowsMaterial == null || settings == null || settings.CloudShadowTexture == null)
            return;

        var cameraData = frameData.Get<UniversalCameraData>();
        var resourceData = frameData.Get<UniversalResourceData>();

        Camera camera = cameraData.camera;

        if (cameraData.cameraType == CameraType.SceneView || !Application.isPlaying)
            return;

        if (resourceData.isActiveTargetBackBuffer)
            return;

        TextureHandle source = resourceData.activeColorTexture;
        TextureHandle depth = resourceData.activeDepthTexture;

        var desc = cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;

        TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_UniStormCloudShadowsDest", false);

        screenSpaceShadowsMaterial.SetMatrix("_CamToWorld", camera.cameraToWorldMatrix);
        screenSpaceShadowsMaterial.SetTexture("_CloudTex", settings.CloudShadowTexture);
        screenSpaceShadowsMaterial.SetFloat("_CloudTexScale", settings.CloudTextureScale + (settings.m_CurrentCloudHeight * 0.000001f) * 2f);
        screenSpaceShadowsMaterial.SetFloat("_CloudHeight", settings.m_CurrentCloudHeight);
        screenSpaceShadowsMaterial.SetFloat("_BottomThreshold", settings.BottomThreshold);
        screenSpaceShadowsMaterial.SetFloat("_TopThreshold", settings.TopThreshold);
        screenSpaceShadowsMaterial.SetFloat("_CloudShadowIntensity", settings.ShadowIntensity);
        screenSpaceShadowsMaterial.SetFloat("_CloudMovementSpeed", settings.CloudSpeed * -0.005f);
        screenSpaceShadowsMaterial.SetVector("_SunDirection", settings.ShadowDirection);

        Vector2 stableSunCloudOffset = GetStableSunCloudOffset();
        screenSpaceShadowsMaterial.SetVector(SunCloudOffsetID, new Vector4(stableSunCloudOffset.x, stableSunCloudOffset.y, 0f, 0f));

        screenSpaceShadowsMaterial.SetFloat("_Fade", settings.Fade);
        screenSpaceShadowsMaterial.SetFloat("_normalY", settings.NormalY <= 0.0001f ? 1f : settings.NormalY);

        Color shadowColor = settings.ShadowColor;
        if (shadowColor == Color.white)
            shadowColor = new Color(0.55f, 0.55f, 0.55f, 1f);

        screenSpaceShadowsMaterial.SetColor("_ShadowColor", shadowColor);

        using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData))
        {
            passData.source = source;
            passData.destination = destination;
            passData.depth = depth;
            passData.material = screenSpaceShadowsMaterial;

            builder.UseTexture(source, AccessFlags.ReadWrite);
            builder.UseTexture(destination, AccessFlags.ReadWrite);
            builder.UseTexture(depth, AccessFlags.Read);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
            {
                CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                RTHandle srcRT = data.source;
                RTHandle dstRT = data.destination;
                RTHandle depthRT = data.depth;

                cmd.SetGlobalTexture(MainTexID, srcRT.nameID);
                cmd.SetGlobalTexture(CameraDepthTextureID, depthRT.nameID);

                cmd.Blit(srcRT.nameID, dstRT.nameID, data.material, 0);
                cmd.Blit(dstRT.nameID, srcRT.nameID);
            });
        }
    }

    public void Dispose()
    {
        CoreUtils.Destroy(screenSpaceShadowsMaterial);
        screenSpaceShadowsMaterial = null;
    }
}
