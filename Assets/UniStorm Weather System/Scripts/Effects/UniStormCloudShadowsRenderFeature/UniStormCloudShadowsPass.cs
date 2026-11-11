using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class UniStormCloudShadowsPass : ScriptableRenderPass
{
    public UniStormCloudShadowsFeature.Settings settings;

    Material screenSpaceShadowsMaterial;

    string m_ProfilerTag;

    public UniStormCloudShadowsPass(string tag)
    {
        m_ProfilerTag = tag;
        Shader unistormCloudShadowsShader = Shader.Find("UniStorm/URP/UniStormCloudShadows");
#if UNITY_EDITOR
        if (unistormCloudShadowsShader == null) return;
#endif
        screenSpaceShadowsMaterial = new Material(unistormCloudShadowsShader);
    }

    private class PassData
    {
        public TextureHandle source;
        public TextureHandle destination;
        public Material material;
        public bool supportsCopyTexture;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
#if UNITY_EDITOR
        if (screenSpaceShadowsMaterial == null || UnityEditor.EditorApplication.isPaused) return;
#endif
        var cameraData = frameData.Get<UniversalCameraData>();
        var resourceData = frameData.Get<UniversalResourceData>();

        Camera camera = cameraData.camera;

        if (cameraData.cameraType == CameraType.SceneView || !Application.isPlaying) return;

        TextureHandle source = resourceData.activeColorTexture;

        var desc = cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_UniStormCloudShadowsDest", false);

        screenSpaceShadowsMaterial.SetMatrix("_CamToWorld", camera.cameraToWorldMatrix);
        screenSpaceShadowsMaterial.SetTexture("_CloudTex", settings.CloudShadowTexture);
        screenSpaceShadowsMaterial.SetFloat("_CloudTexScale", settings.CloudTextureScale + (settings.m_CurrentCloudHeight * 0.000001f) * 2);
        screenSpaceShadowsMaterial.SetFloat("_BottomThreshold", settings.BottomThreshold);
        screenSpaceShadowsMaterial.SetFloat("_TopThreshold", settings.TopThreshold);
        screenSpaceShadowsMaterial.SetFloat("_CloudShadowIntensity", settings.ShadowIntensity);
        screenSpaceShadowsMaterial.SetFloat("_CloudMovementSpeed", settings.CloudSpeed * -0.005f);
        screenSpaceShadowsMaterial.SetVector("_SunDirection", new Vector3(settings.ShadowDirection.x, settings.ShadowDirection.y, settings.ShadowDirection.z));
        screenSpaceShadowsMaterial.SetFloat("_Fade", settings.Fade);
        screenSpaceShadowsMaterial.SetFloat("_normalY", settings.NormalY);

        using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData))
        {
            passData.source = source;
            passData.destination = destination;
            passData.material = screenSpaceShadowsMaterial;
            passData.supportsCopyTexture = (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;

            builder.UseTexture(source, AccessFlags.ReadWrite);
            builder.UseTexture(destination, AccessFlags.ReadWrite);

            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
            {
                CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                RTHandle srcRT = data.source;
                RTHandle dstRT = data.destination;
                cmd.Blit(srcRT.nameID, dstRT.nameID, data.material, 0);
                if (data.supportsCopyTexture)
                    cmd.CopyTexture(dstRT, srcRT);
                else
                    cmd.Blit(dstRT.nameID, srcRT.nameID); //GLES fallback: no flip on GL
            });
        }
    }
}