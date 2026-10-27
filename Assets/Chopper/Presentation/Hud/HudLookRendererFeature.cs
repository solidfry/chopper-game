using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// URP renderer feature: one full-screen pass that draws the HUD texture over the game with the retro look
    /// (<c>Hidden/Chopper/HudLook</c>). Runs only on the last game camera, and only while <see cref="HudLook"/> has bound a
    /// HUD texture (global <see cref="HudLookShaderIds.Enabled"/>), so menus, the scene view and the Clean preset cost nothing.
    /// Chopper → Setup adds it to every URP renderer in the project.
    /// </summary>
    public sealed class HudLookRendererFeature : ScriptableRendererFeature
    {
        public const string ShaderName = "Hidden/Chopper/HudLook";

        [SerializeField, Tooltip("Hidden/Chopper/HudLook. A reference, so builds include the shader.")]
        private Shader shader;

        private Material _material;
        private HudLookPass _pass;

        public Shader Shader => shader;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetShader(Shader value)
        {
            shader = value;
            Create();
        }

        public override void Create()
        {
            CoreUtils.Destroy(_material);
            _material = shader != null ? CoreUtils.CreateEngineMaterial(shader) : null;
            _pass = new HudLookPass(_material) { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null || UnityEngine.Shader.GetGlobalFloat(HudLookShaderIds.Enabled) < 0.5f) return;
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        private sealed class HudLookPass : ScriptableRenderPass
        {
            private const string PassName = "Chopper HUD Look";
            private readonly Material _material;

            public HudLookPass(Material material)
            {
                _material = material;
                // Reads the camera colour and writes a new one: needs an intermediate texture, not the back buffer.
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var camera = frameData.Get<UniversalCameraData>();
                if (camera.cameraType != CameraType.Game || !camera.resolveFinalTarget) return;

                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;

                var source = resources.activeColorTexture;
                var description = renderGraph.GetTextureDesc(source);
                description.name = PassName;
                description.clearBuffer = false;
                var destination = renderGraph.CreateTexture(description);

                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, _material, 0), passName: PassName);
                resources.cameraColor = destination;
            }
        }
    }
}
