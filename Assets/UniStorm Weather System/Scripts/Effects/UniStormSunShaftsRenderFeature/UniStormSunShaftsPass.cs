using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.XR;

public class UniStormSunShaftsPass : ScriptableRenderPass
{
    public UniStormSunShaftsFeature.Settings settings;

    Material sunShaftsMaterial = null;

    string m_ProfilerTag;

    private Transform sunTransform;

    public Transform SunTransform
    {
        get
        {
            if (sunTransform == null)
            {
                Light[] lights = Light.GetLights(LightType.Directional, ~0);
                if (lights.Length > 0)
                {
                    Light sunLight = lights.FirstOrDefault(x => x.name.Equals(settings.celestialName));
                    if (sunLight != null)
                    {
                        sunTransform = sunLight.transform.GetChild(0);
                    }
                }
            }

            return sunTransform;
        }
    }

    public UniStormSunShaftsPass(string tag)
    {
        m_ProfilerTag = tag;
        Shader unistormSunShaftsShader = Shader.Find("UniStorm/URP/UniStormSunShafts");
#if UNITY_EDITOR
        if (unistormSunShaftsShader == null) return;
#endif
        sunShaftsMaterial = new Material(unistormSunShaftsShader);
    }

    private class PassData
    {
        public TextureHandle source;
        public TextureHandle pingBuffer;
        public TextureHandle pongBuffer;
        public TextureHandle finalBuffer;
        public Material material;
        public int radialBlurIterations;
        public float sunShaftBlurRadius;
        public float sunShaftIntensity;
        public float maxRadius;
        public Color sunColor;
        public Color sunThreshold;
        public Vector3 vl;
        public Vector3 vr;
        public int screenBlendMode;
        public bool stereoInstancing;
        public bool stereoMultiview;
        public bool supportsCopyTexture;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
#if UNITY_EDITOR
        if (sunShaftsMaterial == null || UnityEditor.EditorApplication.isPaused) return;
#endif
        var cameraData = frameData.Get<UniversalCameraData>();
        var resourceData = frameData.Get<UniversalResourceData>();

        Camera camera = cameraData.camera;

        if (cameraData.cameraType == CameraType.SceneView || !Application.isPlaying) return;

        RenderTextureDescriptor cameraDesc = cameraData.cameraTargetDescriptor;

        bool stereoInstancing = XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.SinglePassInstanced
            && cameraDesc.volumeDepth == 2;
        bool stereoMultiview = XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.MultiPass
            && camera.stereoActiveEye != Camera.MonoOrStereoscopicEye.Mono && camera.stereoEnabled;

        int divider = 4;
        if (settings.resolution == UniStormSunShaftsFeature.SunShaftsResolution.Normal)
            divider = 2;
        else if (settings.resolution == UniStormSunShaftsFeature.SunShaftsResolution.High)
            divider = 1;

        Vector3 vl = Vector3.one * 0.5f;
        Vector3 vr = Vector3.one * 0.5f;

        Camera.MonoOrStereoscopicEye leftEye;
        if (camera.stereoActiveEye == Camera.MonoOrStereoscopicEye.Left
            && XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.SinglePassInstanced)
        {
            leftEye = Camera.MonoOrStereoscopicEye.Left;
        }
        else
        {
            leftEye = Camera.MonoOrStereoscopicEye.Mono;
        }

        if (SunTransform)
        {
            var sunPosition = SunTransform.position;
            vl = camera.WorldToViewportPoint(sunPosition, leftEye);
            vr = camera.WorldToViewportPoint(sunPosition, Camera.MonoOrStereoscopicEye.Right);
        }
        else
        {
            vl = new Vector3(0.5f, 0.5f, 0.0f);
            vr = new Vector3(0.5f, 0.5f, 0.0f);
        }

        int width = cameraDesc.width;
        int height = cameraDesc.height;
        int rtW = width / divider;
        int rtH = height / divider;

        TextureHandle source = resourceData.activeColorTexture;

        var smallDesc = cameraDesc;
        smallDesc.width = rtW;
        smallDesc.height = rtH;
        smallDesc.depthBufferBits = 0;
        smallDesc.msaaSamples = 1;

        var fullDesc = cameraDesc;
        fullDesc.depthBufferBits = 0;
        //Keep fullDesc.msaaSamples identical to cameraDesc to prevent CopyTexture crashes

        TextureHandle pingBuffer = UniversalRenderer.CreateRenderGraphTexture(renderGraph, smallDesc, "_SunShaftsPing", false);
        TextureHandle pongBuffer = UniversalRenderer.CreateRenderGraphTexture(renderGraph, smallDesc, "_SunShaftsPong", false);
        TextureHandle finalBuffer = UniversalRenderer.CreateRenderGraphTexture(renderGraph, fullDesc, "_SunShaftsFinal", false);

        using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData))
        {
            passData.source = source;
            passData.pingBuffer = pingBuffer;
            passData.pongBuffer = pongBuffer;
            passData.finalBuffer = finalBuffer;
            passData.material = sunShaftsMaterial;
            passData.radialBlurIterations = Mathf.Clamp(settings.radialBlurIterations, 1, 4);
            passData.sunShaftBlurRadius = settings.sunShaftBlurRadius;
            passData.sunShaftIntensity = settings.sunShaftIntensity;
            passData.maxRadius = settings.maxRadius;
            passData.sunColor = settings.sunColor;
            passData.sunThreshold = settings.sunThreshold;
            passData.vl = vl;
            passData.vr = vr;
            passData.screenBlendMode = (int)settings.screenBlendMode;
            passData.stereoInstancing = stereoInstancing;
            passData.stereoMultiview = stereoMultiview;
            passData.supportsCopyTexture = (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;

            builder.UseTexture(source, AccessFlags.ReadWrite);
            builder.UseTexture(pingBuffer, AccessFlags.ReadWrite);
            builder.UseTexture(pongBuffer, AccessFlags.ReadWrite);
            builder.UseTexture(finalBuffer, AccessFlags.ReadWrite);

            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
            {
                CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                if (data.stereoInstancing)
                    cmd.EnableShaderKeyword("STEREO_INSTANCING_ON");
                if (data.stereoMultiview)
                    cmd.EnableShaderKeyword("STEREO_MULTIVIEW_ON");

                Material mat = data.material;
                RTHandle srcRT = data.source;
                RTHandle pingRT = data.pingBuffer;
                RTHandle pongRT = data.pongBuffer;

                mat.SetVector("_BlurRadius4", new Vector4(1.0f, 1.0f, 0.0f, 0.0f) * data.sunShaftBlurRadius);
                mat.SetVectorArray("_SunPositionArray", new Vector4[2]
                {
                    new Vector4(data.vl.x, data.vl.y, data.vl.z, data.maxRadius),
                    new Vector4(data.vr.x, data.vr.y, data.vr.z, data.maxRadius)
                });
                mat.SetVector("_SunThreshold", data.sunThreshold);

                cmd.Blit(srcRT.nameID, pingRT.nameID, mat, 2);

                // Radial blur iterations
                float ofs = data.sunShaftBlurRadius * (1.0f / 768.0f);
                mat.SetVector("_BlurRadius4", new Vector4(ofs, ofs, 0.0f, 0.0f));
                mat.SetVectorArray("_SunPositionArray", new Vector4[2]
                {
                    new Vector4(data.vl.x, data.vl.y, data.vl.z, data.maxRadius),
                    new Vector4(data.vr.x, data.vr.y, data.vr.z, data.maxRadius)
                });

                for (int it2 = 0; it2 < data.radialBlurIterations; it2++)
                {
                    cmd.Blit(pingRT.nameID, pongRT.nameID, mat, 1);

                    ofs = data.sunShaftBlurRadius * (((it2 * 2.0f + 1.0f) * 6.0f)) / 768.0f;
                    mat.SetVector("_BlurRadius4", new Vector4(ofs, ofs, 0.0f, 0.0f));

                    cmd.Blit(pongRT.nameID, pingRT.nameID, mat, 1);

                    ofs = data.sunShaftBlurRadius * (((it2 * 2.0f + 2.0f) * 6.0f)) / 768.0f;
                    mat.SetVector("_BlurRadius4", new Vector4(ofs, ofs, 0.0f, 0.0f));
                }

                if (data.vl.z >= 0.0f)
                    mat.SetVector("_SunColor", (Vector4)(data.sunColor * data.sunShaftIntensity));
                else
                    mat.SetVector("_SunColor", Vector4.zero);

                mat.SetTexture("_ColorBuffer", pingRT.rt);

                RTHandle finalRT = data.finalBuffer;
                cmd.Blit(srcRT.nameID, finalRT.nameID, mat,
                    (data.screenBlendMode == 0) ? 0 : 4);
                if (data.supportsCopyTexture)
                    cmd.CopyTexture(finalRT, srcRT);
                else
                    cmd.Blit(finalRT.nameID, srcRT.nameID); //GLES fallback: no flip on GL

                cmd.DisableShaderKeyword("STEREO_INSTANCING_ON");
                cmd.DisableShaderKeyword("STEREO_MULTIVIEW_ON");
            });
        }
    }
}