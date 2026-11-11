using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace UniStorm.Utility
{
    public class UniStormCloudsRendererFeature : ScriptableRendererFeature
    {
        class UniStormCloudsRenderPass : ScriptableRenderPass
        {
            private UniStormClouds m_UniStormClouds;
            private ProfilingSampler m_ProfilingSampler = new ProfilingSampler("UniStorm Clouds");

            public UniStormCloudsRenderPass(UniStormClouds uniStormClouds)
            {
                this.m_UniStormClouds = uniStormClouds;
                renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
            }

            private class PassData
            {
                public UniStormClouds clouds;
                public ProfilingSampler profilingSampler;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
#if UNITY_EDITOR
                if (UnityEditor.EditorApplication.isPaused) return;
#endif
                if (m_UniStormClouds == null || !m_UniStormClouds.enabled) return;

                var cameraData = frameData.Get<UniversalCameraData>();

                if (cameraData.cameraType == CameraType.SceneView || !Application.isPlaying) return;

                if (!UniStormSystem.Instance.UniStormInitialized) return;

                using (var builder = renderGraph.AddUnsafePass<PassData>("UniStorm Clouds", out var passData))
                {
                    passData.clouds = m_UniStormClouds;
                    passData.profilingSampler = m_ProfilingSampler;

                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        var clouds = data.clouds;

                        using (new ProfilingScope(cmd, data.profilingSampler))
                        {
                            //1. Render the first clouds buffer - lower resolution
                            cmd.Blit(null, clouds.lowResCloudsBuffer, clouds.skyMaterial, 0);

                            //2. Blend between low and hi-res
                            cmd.SetGlobalTexture("_uLowresCloudTex", clouds.lowResCloudsBuffer);
                            cmd.SetGlobalTexture("_uPreviousCloudTex", clouds.fullCloudsBuffer[clouds.fullBufferIndex]);
                            cmd.Blit(clouds.fullCloudsBuffer[clouds.fullBufferIndex], clouds.fullCloudsBuffer[clouds.fullBufferIndex ^ 1], clouds.skyMaterial, 1);

                            switch (clouds.CloudShadowsTypeRef)
                            {
                                case UniStormClouds.CloudShadowsType.Off:
                                    break;
                                case UniStormClouds.CloudShadowsType.Simulated:
                                    clouds.shadowsBuildingMaterial.SetFloat("_uCloudsCoverage", clouds.skyMaterial.GetFloat("_uCloudsCoverage"));
                                    clouds.shadowsBuildingMaterial.SetFloat("_uCloudsCoverageBias", clouds.skyMaterial.GetFloat("_uCloudsCoverageBias"));
                                    clouds.shadowsBuildingMaterial.SetFloat("_uCloudsDensity", clouds.skyMaterial.GetFloat("_uCloudsDensity"));
                                    clouds.shadowsBuildingMaterial.SetFloat("_uCloudsDetailStrength", clouds.skyMaterial.GetFloat("_uCloudsDetailStrength"));
                                    clouds.shadowsBuildingMaterial.SetFloat("_uCloudsBaseEdgeSoftness", clouds.skyMaterial.GetFloat("_uCloudsBaseEdgeSoftness"));
                                    clouds.shadowsBuildingMaterial.SetFloat("_uCloudsBottomSoftness", clouds.skyMaterial.GetFloat("_uCloudsBottomSoftness"));
                                    clouds.shadowsBuildingMaterial.SetFloat("_uSimulatedCloudAlpha", clouds.cloudTransparency);
                                    cmd.Blit(GenerateNoise.baseNoiseTexture, clouds.cloudShadowsBuffer[0], clouds.shadowsBuildingMaterial, 3);
                                    clouds.PublicCloudShadowTexture = clouds.cloudShadowsBuffer[0];
                                    break;
                                case UniStormClouds.CloudShadowsType.RealTime:
                                    cmd.Blit(clouds.fullCloudsBuffer[clouds.fullBufferIndex ^ 1], clouds.cloudShadowsBuffer[0]);
                                    for (int i = 0; i < clouds.shadowBlurIterations; i++)
                                    {
                                        cmd.Blit(clouds.cloudShadowsBuffer[0], clouds.cloudShadowsBuffer[1], clouds.shadowsBuildingMaterial, 1);
                                        cmd.Blit(clouds.cloudShadowsBuffer[1], clouds.cloudShadowsBuffer[0], clouds.shadowsBuildingMaterial, 2);
                                    }
                                    break;
                                default:
                                    break;
                            }

                            cmd.SetGlobalFloat("_uLightning", 0.0f);

                            //3. Set to material for the sky (not in the command buffer)
                            clouds.cloudsMaterial.SetTexture("_MainTex", clouds.fullCloudsBuffer[clouds.fullBufferIndex ^ 1]);
                        }
                    });
                }
            }
        }

        UniStormCloudsRenderPass m_ScriptablePass;

        public override void Create()
        {
            
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            UniStormClouds uniStormClouds = FindAnyObjectByType<UniStormClouds>();
            if (uniStormClouds == null)
                return;

            if (m_ScriptablePass == null)
            {
                m_ScriptablePass = new UniStormCloudsRenderPass(uniStormClouds);
            }

            renderer.EnqueuePass(m_ScriptablePass);
        }
    }
}