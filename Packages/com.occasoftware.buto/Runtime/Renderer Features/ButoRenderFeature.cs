using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OccaSoftware.Buto.Runtime
{
    public class ButoRenderFeature : ScriptableRendererFeature
    {
        class RenderFogPass : ScriptableRenderPass
        {
            private RTHandle source;
            private RenderTextureDescriptor fogTargetDescriptor;
            private RenderTextureDescriptor descriptor;

            private RTHandle fogTarget;
            private RTHandle butoResultsTarget;
            private RTHandle mergeTarget;
            private RTHandle lowResDepthTarget;
            private RTHandle upsampleTarget;

            private ButoVolumetricFog volumetricFog = null;
            private Material fogMaterial = null;
            private Material mergeMaterial = null;
            private Material depthMaterial = null;
            private Material upscaleMaterial = null;
            private const string fogShaderPath = "OccaSoftware/Buto/RenderFog";
            private const string mergeShaderPath = "OccaSoftware/Buto/Merge";
            private const string depthDownscaleShaderPath = "OccaSoftware/Buto/DownsampleDepthTexture";
            private const string upscaleShaderPath = "OccaSoftware/Buto/DepthAwareUpscale";

            private const string butoTextureId = "_ButoTexture";
            private const string upscaleInputTexId = "_ButoUpscaleInputTex";
            private const string lowResDepthTexId = "_ButoDepth";
            private const string upsampleTargetId = "_ButoUpsampleTargetId";

            private const string passIdentifier = "Buto";

            private bool isFirst = true;

            #region Temporal AA
            private const string taaInputTexId = "_BUTO_HISTORY_TEX";
            private Material taaMaterial = null;
            private const string taaShaderPath = "OccaSoftware/Buto/TemporalAA";
            private RTHandle taaTarget;

            Dictionary<Camera, TAACameraData> renderTextures;
            float managedTime;
            uint frameCount;

            class TAACameraData
            {
                private uint lastFrameUsed;
                private RenderTexture renderTexture;
                private string cameraName;

                public TAACameraData(uint lastFrameUsed, RenderTexture renderTexture, string cameraName)
                {
                    LastFrameUsed = lastFrameUsed;
                    RenderTexture = renderTexture;
                    CameraName = cameraName;
                }

                public uint LastFrameUsed
                {
                    get => lastFrameUsed;
                    set => lastFrameUsed = value;
                }

                public RenderTexture RenderTexture
                {
                    get => renderTexture;
                    set => renderTexture = value;
                }

                public string CameraName
                {
                    get => cameraName;
                    set => cameraName = value;
                }
            }

            void CalculateTime()
            {
                // Get data
                float unityRealtimeSinceStartup = Time.realtimeSinceStartup;
                uint unityFrameCount = (uint)Time.frameCount;

                bool newFrame;
                if (Application.isPlaying)
                {
                    newFrame = frameCount != unityFrameCount;
                    frameCount = unityFrameCount;
                }
                else
                {
                    newFrame = (unityRealtimeSinceStartup - managedTime) > 0.0166f;
                    if (newFrame)
                        frameCount++;
                }

                if (newFrame)
                {
                    managedTime = unityRealtimeSinceStartup;
                }
            }

            void GetTemporalAARenderTexture(Camera camera, RenderTextureDescriptor descriptor)
            {
                if (renderTextures.TryGetValue(camera, out TAACameraData cameraData))
                {
                    CheckRenderTextureScale(camera, descriptor, cameraData.RenderTexture);
                }
                else
                {
                    CreateTAARenderTextureAndAddToDictionary(camera, descriptor);
                }
            }

            void CheckRenderTextureScale(Camera camera, RenderTextureDescriptor descriptor, RenderTexture renderTexture)
            {
                if (renderTexture == null)
                {
                    CreateTAARenderTextureAndAddToDictionary(camera, descriptor);
                    return;
                }

                bool rtWrongSize = (renderTexture.width != descriptor.width || renderTexture.height != descriptor.height) ? true : false;
                if (rtWrongSize)
                {
                    CreateTAARenderTextureAndAddToDictionary(camera, descriptor);
                    return;
                }
            }

            void CreateTAARenderTextureAndAddToDictionary(Camera camera, RenderTextureDescriptor descriptor)
            {
                SetupTAARenderTexture(camera, descriptor, out RenderTexture renderTexture);

                if (renderTextures.ContainsKey(camera))
                {
                    if (renderTextures[camera].RenderTexture != null)
                        renderTextures[camera].RenderTexture.Release();

                    renderTextures[camera].RenderTexture = renderTexture;
                }
                else
                {
                    renderTextures.Add(camera, new TAACameraData(frameCount, renderTexture, camera.name));
                }
            }

            void SetupTAARenderTexture(Camera camera, RenderTextureDescriptor descriptor, out RenderTexture renderTexture)
            {
                descriptor.colorFormat = RenderTextureFormat.DefaultHDR;
                descriptor.msaaSamples = 1;
                descriptor.depthBufferBits = 0;
                descriptor.width = Mathf.Max(1, descriptor.width);
                descriptor.height = Mathf.Max(1, descriptor.height);

                renderTexture = new RenderTexture(descriptor);

                RenderTexture activeTexture = RenderTexture.active;
                RenderTexture.active = renderTexture;
                GL.Clear(false, true, new Color(0.0f, 0.0f, 0.0f, 1.0f));
                RenderTexture.active = activeTexture;

                renderTexture.name = camera.name + " Buto TAA History";
                renderTexture.filterMode = FilterMode.Bilinear;
                renderTexture.wrapMode = TextureWrapMode.Clamp;

                renderTexture.Create();
            }

            List<Camera> removeTargets = new List<Camera>();

            void CleanupDictionary()
            {
                removeTargets.Clear();
                foreach (KeyValuePair<Camera, TAACameraData> entry in renderTextures)
                {
                    if (entry.Value.LastFrameUsed < frameCount - 10)
                    {
                        if (entry.Value.RenderTexture != null)
                            entry.Value.RenderTexture.Release();

                        removeTargets.Add(entry.Key);
                    }
                }

                for (int i = 0; i < removeTargets.Count; i++)
                {
                    renderTextures.Remove(removeTargets[i]);
                }
            }
            #endregion

            internal void SetupMaterials()
            {
                GetShaderAndSetupMaterial(fogShaderPath, ref fogMaterial);
                GetShaderAndSetupMaterial(mergeShaderPath, ref mergeMaterial);
                GetShaderAndSetupMaterial(depthDownscaleShaderPath, ref depthMaterial);
                GetShaderAndSetupMaterial(taaShaderPath, ref taaMaterial);
                GetShaderAndSetupMaterial(upscaleShaderPath, ref upscaleMaterial);
            }

            void GetShaderAndSetupMaterial(string path, ref Material target)
            {
                if (target != null)
                    return;

                Shader s = Shader.Find(path);
                if (s != null)
                {
                    target = CoreUtils.CreateEngineMaterial(s);
                }
                else
                {
                    Debug.Log("Buto missing shader reference at " + path);
                }
            }

            public RenderFogPass()
            {
                renderTextures = new Dictionary<Camera, TAACameraData>();

                fogTarget = RTHandles.Alloc(Shader.PropertyToID("os_LowResFogTarget"), name: "os_LowResFogTarget");
                taaTarget = RTHandles.Alloc(Shader.PropertyToID("Buto TAA Target"), name: "Buto TAA Target");
                lowResDepthTarget = RTHandles.Alloc(Shader.PropertyToID(lowResDepthTexId), name: lowResDepthTexId);
                upsampleTarget = RTHandles.Alloc(Shader.PropertyToID(upsampleTargetId), name: upsampleTargetId);
                mergeTarget = RTHandles.Alloc(Shader.PropertyToID("Buto Final Merge Target"), name: "Buto Final Merge Target");
            }

            public bool RegisterStackComponent()
            {
                volumetricFog = VolumeManager.instance.stack.GetComponent<ButoVolumetricFog>();

                if (volumetricFog == null)
                    return false;

                return volumetricFog.IsActive();
            }

            public void SetTarget(RTHandle colorHandle)
            {
                source = colorHandle;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                ConfigureTarget(source);

                descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.msaaSamples = 1;
                descriptor.depthBufferBits = 0;
                descriptor.colorFormat = RenderTextureFormat.DefaultHDR;
                descriptor.width = Mathf.Max(1, descriptor.width);
                descriptor.height = Mathf.Max(1, descriptor.height);

                fogTargetDescriptor = descriptor;

                fogTargetDescriptor.width = fogTargetDescriptor.width >> 1;
                fogTargetDescriptor.height = fogTargetDescriptor.height >> 1;

                RenderTextureDescriptor depthDescriptor = fogTargetDescriptor;
                depthDescriptor.sRGB = false;
                depthDescriptor.colorFormat = RenderTextureFormat.RFloat;

                RenderingUtils.ReAllocateIfNeeded(
                    ref fogTarget,
                    fogTargetDescriptor,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: "os_LowResFogTarget"
                );
                RenderingUtils.ReAllocateIfNeeded(
                    ref lowResDepthTarget,
                    depthDescriptor,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: lowResDepthTexId
                );
                RenderingUtils.ReAllocateIfNeeded(ref upsampleTarget, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: upsampleTargetId);
                RenderingUtils.ReAllocateIfNeeded(ref taaTarget, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "Buto TAA Target");
                RenderingUtils.ReAllocateIfNeeded(
                    ref mergeTarget,
                    descriptor,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: "Buto Final Merge Target"
                );
            }

            internal bool IsTaaEnabled(CameraType cameraType)
            {
                if (cameraType != CameraType.Game)
                    return false;

                if (!volumetricFog.temporalAntiAliasingEnabled.value)
                    return false;

                if (!Application.isPlaying)
                    return false;

                return true;
            }

            internal bool HasAllMaterials()
            {
                if (fogMaterial != null && depthMaterial != null && mergeMaterial != null && taaMaterial != null)
                    return true;

                return false;
            }

            Vector4[] volumePosition = new Vector4[ButoCommon._MAXVOLUMECOUNT];
            Vector4[] size = new Vector4[ButoCommon._MAXVOLUMECOUNT];
            float[] intensity = new float[ButoCommon._MAXVOLUMECOUNT];
            float[] blendMode = new float[ButoCommon._MAXVOLUMECOUNT];
            float[] blendDistance = new float[ButoCommon._MAXVOLUMECOUNT];
            float[] shape = new float[ButoCommon._MAXVOLUMECOUNT];

            Vector4[] lightPosition = new Vector4[ButoCommon._MAXLIGHTCOUNT];
            float[] lightStrength = new float[ButoCommon._MAXLIGHTCOUNT];
            Vector4[] color = new Vector4[ButoCommon._MAXLIGHTCOUNT];
            Vector4[] direction = new Vector4[ButoCommon._MAXLIGHTCOUNT];
            Vector4[] angle = new Vector4[ButoCommon._MAXLIGHTCOUNT];

            Vector4[] simpleColorSet = new Vector4[3];

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                UnityEngine.Profiling.Profiler.BeginSample(passIdentifier);
                CommandBuffer cmd = CommandBufferPool.Get(passIdentifier);

                CalculateTime();
                CleanupDictionary();

                cmd.SetGlobalTexture("_ScreenTexture", source.nameID);
                Camera camera = renderingData.cameraData.camera;
                volumetricFog = VolumeManager.instance.stack.GetComponent<ButoVolumetricFog>();

                SetMaterialParameters(cmd, camera);

#if UNITY_WEBGL
				fogMaterial.EnableKeyword("_IS_UNITY_WEBGL");
#else
                fogMaterial.DisableKeyword("_IS_UNITY_WEBGL");
#endif

                // Sets up low res depth texture
                Blitter.BlitCameraTexture(cmd, source, lowResDepthTarget, depthMaterial, 0);

                // Samples volumetric fog
                cmd.SetGlobalTexture("_ButoDepth", lowResDepthTarget.nameID);
                Blitter.BlitCameraTexture(cmd, source, fogTarget, fogMaterial, 0);

                // Upsample the volumetric fog
                cmd.SetGlobalTexture(upscaleInputTexId, fogTarget.nameID);
                Blitter.BlitCameraTexture(cmd, source, upsampleTarget, upscaleMaterial, 0);

                // Set Buto Texture
                cmd.SetGlobalTexture(upsampleTargetId, upsampleTarget.nameID);
                cmd.SetGlobalTexture(butoTextureId, upsampleTarget.nameID);

                // Do TAA
                if (IsTaaEnabled(camera.cameraType))
                {
                    GetTemporalAARenderTexture(renderingData.cameraData.camera, descriptor);
                    if (renderTextures[camera].RenderTexture != null)
                    {
                        cmd.SetGlobalTexture(taaInputTexId, RTHandles.Alloc(renderTextures[camera].RenderTexture));
                    }
                    Blitter.BlitCameraTexture(cmd, source, taaTarget, taaMaterial, 0);

                    if (renderTextures[camera].RenderTexture == null)
                    {
                        Debug.Log(
                            "Buto Temporal AA Render Texture is missing. Please submit bug report. Missing Texture: "
                                + renderTextures[camera].CameraName
                                + " RT"
                        );
                    }
                    else
                    {
                        Blitter.BlitCameraTexture(cmd, taaTarget, RTHandles.Alloc(renderTextures[camera].RenderTexture));
                        renderTextures[camera].LastFrameUsed = frameCount;
                    }

                    cmd.SetGlobalTexture(butoTextureId, taaTarget.nameID);
                }

                // Merges with scene
                Blitter.BlitCameraTexture(cmd, source, mergeTarget, mergeMaterial, 0);

                // Blit to screen.
                Blitter.BlitCameraTexture(cmd, mergeTarget, source);

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
                UnityEngine.Profiling.Profiler.EndSample();
            }

            void SetMaterialParameters(CommandBuffer cmd, Camera camera)
            {
                SetSphericalHarmonics(cmd);
                SetFogMaterialData(cmd);
                SetAdditionalLightData(cmd, camera);
                SetVolumeData(cmd, camera);
                SetTaaIntegrationRate(cmd);
            }

            void SetFogMaterialData(CommandBuffer cmd)
            {
                cmd.SetGlobalInt(Params.SampleCount.Id, volumetricFog.sampleCount.value);

                cmd.SetGlobalFloat(Params._RayLengthMode.Id, (float)volumetricFog.rayLengthMode.value);
                cmd.SetGlobalFloat(Params._DistantFogStartDistance.Id, volumetricFog.distantFogStartDistance.value);
                cmd.SetGlobalFloat(Params.DepthSofteningDistance.Id, volumetricFog.depthSofteningDistance.value);
                cmd.SetGlobalFloat(Params.DistantFogDensity.Id, volumetricFog.distantFogDensity.value);
                cmd.SetGlobalInt(Params.AnimateSamplePosition.Id, BoolToInt(GetSamplePositionAnimationState()));
                cmd.SetGlobalInt(Params.MaximumSelfShadowingOctaves.Id, volumetricFog.maximumSelfShadowingOctaves.value);

                cmd.SetKeyword(
                    fogMaterial,
                    new LocalKeyword(fogMaterial.shader, Params.EnableHorizonShadowing.Property),
                    volumetricFog.horizonShadowingEnabled.value
                );

                cmd.SetKeyword(
                    fogMaterial,
                    new LocalKeyword(fogMaterial.shader, Params.EnableSelfShadowing.Property),
                    volumetricFog.selfShadowingEnabled.value
                );

                cmd.SetKeyword(
                    fogMaterial,
                    new LocalKeyword(fogMaterial.shader, Params.AnalyticFogEnabled.Property),
                    volumetricFog.analyticFogEnabled.value
                );

                cmd.SetGlobalFloat(Params.DepthInteractionMode.Property, (float)volumetricFog.depthInteractionMode.value); // 0 = Early exit, 1 = Shorten ray length to maximize sampling
                cmd.SetGlobalInt(Params.FrameId.Property, Time.frameCount);

                cmd.SetGlobalFloat(Params.MaxDistanceVolumetric.Id, volumetricFog.maxDistanceVolumetric.value);
                cmd.SetGlobalFloat(Params.MaxDistanceNonVolumetric.Id, volumetricFog.maxDistanceAnalytic.value);

                cmd.SetGlobalFloat(Params.Anisotropy.Id, volumetricFog.anisotropy.value);
                cmd.SetGlobalFloat(Params.BaseHeight.Id, volumetricFog.baseHeight.value);
                cmd.SetGlobalFloat(Params.AttenuationBoundarySize.Id, volumetricFog.attenuationBoundarySize.value);

                cmd.SetGlobalFloat(Params.DistantAttenuationBoundarySize.Id, volumetricFog.distantAttenuationBoundarySize.value);

                cmd.SetGlobalFloat(Params.DistantBaseHeight.Id, volumetricFog.distantBaseHeight.value);

                cmd.SetGlobalFloat(Params.FogDensity.Id, volumetricFog.fogDensity.value);
                cmd.SetGlobalFloat(Params.LightIntensity.Id, volumetricFog.lightIntensity.value);
                cmd.SetGlobalFloat(Params.ShadowIntensity.Id, volumetricFog.shadowIntensity.value);

                cmd.SetGlobalTexture(Params.ColorRamp.Id, volumetricFog.colorRamp.value);

                simpleColorSet[0] = (Vector4)volumetricFog.litColor.value;
                simpleColorSet[1] = (Vector4)volumetricFog.shadowedColor.value;
                simpleColorSet[2] = (Vector4)volumetricFog.emitColor.value;
                cmd.SetGlobalVectorArray(Params.SimpleColor.Id, simpleColorSet);
                cmd.SetGlobalFloat(Params.ColorInfluence.Id, volumetricFog.colorInfluence.value);

                cmd.SetGlobalTexture(Params.NoiseTexture.Id, volumetricFog.volumeNoise.value.GetTexture());

                cmd.SetGlobalInt(Params.Octaves.Id, volumetricFog.octaves.value);
                cmd.SetGlobalFloat(Params.NoiseTiling.Id, volumetricFog.noiseTiling.value);
                cmd.SetGlobalVector(Params.NoiseWindSpeed.Id, volumetricFog.noiseWindSpeed.value);
                cmd.SetGlobalFloat(Params.NoiseIntensityMin.Id, volumetricFog.noiseMap.value.x);
                cmd.SetGlobalFloat(Params.NoiseIntensityMax.Id, volumetricFog.noiseMap.value.y);
                cmd.SetGlobalFloat(Params.Lacunarity.Id, volumetricFog.lacunarity.value);
                cmd.SetGlobalFloat(Params.Gain.Id, volumetricFog.gain.value);

                cmd.SetGlobalVector(Params.DirectionalLightingForward.Id, volumetricFog.directionalForward.value);
                cmd.SetGlobalVector(Params.DirectionalLightingBack.Id, volumetricFog.directionalBack.value);
                cmd.SetGlobalFloat(Params.DirectionalLightingRatio.Id, volumetricFog.directionalRatio.value);
            }

            bool GetSamplePositionAnimationState()
            {
                bool animateSamplePosition = false;
                bool isAnimateSamplePositionNotOverriddenAndDisabledButTaaIsEnabled =
                    !volumetricFog.animateSamplePosition.overrideState
                    && !volumetricFog.animateSamplePosition.value
                    && volumetricFog.temporalAntiAliasingEnabled.value;
                bool isAnimateSamplePositionEnabled = volumetricFog.animateSamplePosition.value;
                if (isAnimateSamplePositionEnabled || isAnimateSamplePositionNotOverriddenAndDisabledButTaaIsEnabled)
                    animateSamplePosition = true;

                return animateSamplePosition;
            }

            void SetTaaIntegrationRate(CommandBuffer cmd)
            {
                float taaRate = volumetricFog.temporalAntiAliasingIntegrationRate.value;
                if (isFirst)
                {
                    isFirst = false;
                    taaRate = 1;
                }

                cmd.SetGlobalFloat(Params.TemporalAaIntegrationRate.Id, taaRate);
            }

            void SetAdditionalLightData(CommandBuffer cmd, Camera camera)
            {
                if (ButoLight.Lights.Count > ButoCommon._MAXLIGHTCOUNT)
                    ButoLight.SortByDistance(camera.transform.position);

                int lightCount = Mathf.Min(ButoLight.Lights.Count, ButoCommon._MAXLIGHTCOUNT);
                for (int i = 0; i < lightCount; i++)
                {
                    lightPosition[i] = ButoLight.Lights[i].LightPosition;
                    lightStrength[i] = ButoLight.Lights[i].LightIntensity;
                    color[i] = ButoLight.Lights[i].LightColor;
                    direction[i] = ButoLight.Lights[i].LightDirection;
                    angle[i] = ButoLight.Lights[i].GetLightAngles();
                }

                fogMaterial.SetInt(Params.LightCountButo.Id, lightCount);
                if (lightCount > 0)
                {
                    cmd.SetGlobalVectorArray(Params.LightPosButo.Id, lightPosition);
                    cmd.SetGlobalFloatArray(Params.LightIntensityButo.Id, lightStrength);
                    cmd.SetGlobalVectorArray(Params.LightColorButo.Id, color);
                    cmd.SetGlobalVectorArray(Params.LightDirectionButo.Id, direction);
                    cmd.SetGlobalVectorArray(Params.LightAngleButo.Id, angle);
                }
            }

            void SetVolumeData(CommandBuffer cmd, Camera camera)
            {
                if (FogDensityMask.FogVolumes.Count > ButoCommon._MAXVOLUMECOUNT)
                    FogDensityMask.SortByDistance(camera.transform.position);

                int volumeCount = Mathf.Min(FogDensityMask.FogVolumes.Count, ButoCommon._MAXVOLUMECOUNT);
                for (int i = 0; i < volumeCount; i++)
                {
                    volumePosition[i] = (Vector4)FogDensityMask.FogVolumes[i].transform.position;
                    size[i] = (Vector4)FogDensityMask.FogVolumes[i].Size;
                    shape[i] = (int)FogDensityMask.FogVolumes[i].Shape;
                    intensity[i] = FogDensityMask.FogVolumes[i].DensityMultiplier;
                    blendMode[i] = (int)FogDensityMask.FogVolumes[i].Mode;
                    blendDistance[i] = FogDensityMask.FogVolumes[i].BlendDistance;
                }

                fogMaterial.SetInt(Params.VolumeCountButo.Id, volumeCount);
                if (volumeCount > 0)
                {
                    cmd.SetGlobalVectorArray(Params.VolumePosition.Id, volumePosition);
                    cmd.SetGlobalVectorArray(Params.VolumeSize.Id, size);
                    cmd.SetGlobalFloatArray(Params.VolumeIntensity.Id, intensity);
                    cmd.SetGlobalFloatArray(Params.VolumeBlendMode.Id, blendMode);
                    cmd.SetGlobalFloatArray(Params.VolumeBlendDistance.Id, blendDistance);
                    cmd.SetGlobalFloatArray(Params.VolumeShape.Id, shape);
                }
            }

            private void SetSphericalHarmonics(CommandBuffer cmd)
            {
                SphericalHarmonicsL2 sh2 = RenderSettings.ambientProbe;
                Color ambient = new Color(sh2[0, 0] - sh2[0, 6], sh2[1, 0] - sh2[1, 6], sh2[2, 0] - sh2[2, 6]);
                cmd.SetGlobalColor(Params.WorldColor.Id, ambient);
            }

            int BoolToInt(bool a)
            {
                return a == false ? 0 : 1;
            }
        }

        [System.Serializable]
        public class Settings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        public Settings settings = new Settings();

        RenderFogPass renderFogPass;

        public override void Create()
        {
            renderFogPass = new RenderFogPass();
            renderFogPass.renderPassEvent = settings.renderPassEvent;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Shader.SetGlobalFloat(Params.ButoIsEnabled.Id, 0f);
            if (renderingData.cameraData.camera.cameraType == CameraType.Reflection)
                return;

#if UNITY_EDITOR
            bool isSceneCamera = renderingData.cameraData.camera.cameraType == CameraType.SceneView ? true : false;

            if (isSceneCamera)
            {
                bool fogEnabled = UnityEditor.SceneView.currentDrawingSceneView.sceneViewState.fogEnabled;

                bool isDrawingTextured =
                    UnityEditor.SceneView.currentDrawingSceneView.cameraMode.drawMode == UnityEditor.DrawCameraMode.Textured ? true : false;
                if (!fogEnabled || !isDrawingTextured)
                    return;
            }
#endif

            if (!renderingData.cameraData.postProcessEnabled)
                return;

            if (!renderFogPass.RegisterStackComponent())
                return;

            if (renderingData.cameraData.camera.TryGetComponent<DisableButoRendering>(out _))
                return;

            renderer.EnqueuePass(renderFogPass);
            Shader.SetGlobalFloat(Params.ButoIsEnabled.Id, 1f);
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            // Maybe do this differently?
            renderFogPass.SetupMaterials();
            renderFogPass.ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Motion);
            renderFogPass.SetTarget(renderer.cameraColorTargetHandle);
        }

        private static class Params
        {
            public readonly struct Param
            {
                public Param(string property)
                {
                    Property = property;
                    Id = Shader.PropertyToID(property);
                }

                readonly public string Property;
                readonly public int Id;
            }

            public static Param ButoIsEnabled = new Param("_ButoIsEnabled");

            public static Param SampleCount = new Param("_SampleCount");
            public static Param AnimateSamplePosition = new Param("_AnimateSamplePosition");
            public static Param DepthInteractionMode = new Param("_DepthInteractionMode");
            public static Param _RayLengthMode = new Param("_RayLengthMode");
            public static Param FrameId = new Param("_FrameId");

            public static Param EnableSelfShadowing = new Param("_BUTO_SELF_ATTENUATION_ENABLED");
            public static Param EnableHorizonShadowing = new Param("_BUTO_HORIZON_SHADOWING_ENABLED");

            public static Param MaxDistanceVolumetric = new Param("_MaxDistanceVolumetric");
            public static Param AnalyticFogEnabled = new Param("_BUTO_ANALYTIC_FOG_ENABLED");
            public static Param MaxDistanceNonVolumetric = new Param("_MaxDistanceNonVolumetric");
            public static Param MaximumSelfShadowingOctaves = new Param("_MaximumSelfShadowingOctaves");
            public static Param FogMaskBlendMode = new Param("_FogMaskBlendMode");
            public static Param DistantFogDensity = new Param("_DistantFogDensity");
            public static Param DepthSofteningDistance = new Param("_DepthSofteningDistance");
            public static Param _DistantFogStartDistance = new Param("_DistantFogStartDistance");

            // TAA Param
            public static Param TemporalAaIntegrationRate = new Param("_IntegrationRate");

            public static Param FogDensity = new Param("_FogDensity");
            public static Param Anisotropy = new Param("_Anisotropy");
            public static Param LightIntensity = new Param("_LightIntensity");
            public static Param ShadowIntensity = new Param("_ShadowIntensity");

            public static Param BaseHeight = new Param("_BaseHeight");
            public static Param AttenuationBoundarySize = new Param("_AttenuationBoundarySize");

            public static Param DistantBaseHeight = new Param("_DistantBaseHeight");
            public static Param DistantAttenuationBoundarySize = new Param("_DistantAttenuationBoundarySize");

            public static Param ColorRamp = new Param("_ColorRamp");
            public static Param SimpleColor = new Param("_SimpleColor");
            public static Param ColorInfluence = new Param("_ColorInfluence");

            public static Param NoiseTexture = new Param("_NoiseTexture");
            public static Param Octaves = new Param("_Octaves");
            public static Param Lacunarity = new Param("_Lacunarity");
            public static Param Gain = new Param("_Gain");
            public static Param NoiseTiling = new Param("_NoiseTiling");
            public static Param NoiseWindSpeed = new Param("_NoiseWindSpeed");
            public static Param NoiseIntensityMin = new Param("_NoiseIntensityMin");
            public static Param NoiseIntensityMax = new Param("_NoiseIntensityMax");

            public static Param LightCountButo = new Param("_LightCountButo");
            public static Param LightPosButo = new Param("_LightPosButo");
            public static Param LightIntensityButo = new Param("_LightIntensityButo");
            public static Param LightColorButo = new Param("_LightColorButo");
            public static Param LightDirectionButo = new Param("_LightDirectionButo");
            public static Param LightAngleButo = new Param("_LightAngleButo");

            // Volume Data
            public static Param VolumeCountButo = new Param("_VolumeCountButo");
            public static Param VolumePosition = new Param("_VolumePosition");
            public static Param VolumeSize = new Param("_VolumeSize");
            public static Param VolumeShape = new Param("_VolumeShape");
            public static Param VolumeIntensity = new Param("_VolumeIntensityButo");
            public static Param VolumeBlendMode = new Param("_VolumeBlendMode");
            public static Param VolumeBlendDistance = new Param("_VolumeBlendDistance");

            public static Param DirectionalLightingForward = new Param("_DirectionalLightingForward");
            public static Param DirectionalLightingBack = new Param("_DirectionalLightingBack");
            public static Param DirectionalLightingRatio = new Param("_DirectionalLightingRatio");

            // Ambient Lighting
            public static Param WorldColor = new Param("_WorldColor");
        }
    }
}
