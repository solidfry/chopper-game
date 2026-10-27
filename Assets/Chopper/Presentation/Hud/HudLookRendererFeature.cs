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
    /// Phosphor persistence: a HUD-size history texture (two, ping-pong) keeps a fading trail of the HUD, drawn under it.
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
            _pass?.Dispose();
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
            _pass?.Dispose();
            _pass = null;
            CoreUtils.Destroy(_material);
            _material = null;
        }

        private sealed class HudLookPass : ScriptableRenderPass
        {
            private const string PassName = "Chopper HUD Look";
            private const string PersistenceName = "Chopper HUD Persistence";
            private const int CompositePass = 0;
            private const int PersistencePass = 1;
            private static readonly int HistoryId = UnityEngine.Shader.PropertyToID("_ChopperHudHistory");

            private readonly Material _material;
            private readonly RTHandle[] _history = new RTHandle[2];
            private int _read;

            private sealed class CompositeData
            {
                public Material Material;
                public TextureHandle Source;
                public TextureHandle History;
                public bool HasHistory;
            }

            public HudLookPass(Material material)
            {
                _material = material;
                // Reads the camera colour and writes a new one: needs an intermediate texture, not the back buffer.
                requiresIntermediateTexture = true;
            }

            public void Dispose()
            {
                for (var i = 0; i < _history.Length; i++)
                {
                    _history[i]?.Release();
                    _history[i] = null;
                }
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

                // Trail: history = max(HUD, last history × decay), in HUD texture space.
                var history = TextureHandle.nullHandle;
                if (UnityEngine.Shader.GetGlobalVector(HudLookShaderIds.Params3).y > 0f && AllocateHistory())
                {
                    var read = renderGraph.ImportTexture(_history[_read]);
                    var write = renderGraph.ImportTexture(_history[1 - _read]);
                    _read = 1 - _read;
                    renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(read, write, _material, PersistencePass),
                        passName: PersistenceName);
                    history = write;
                }

                using (var builder = renderGraph.AddRasterRenderPass<CompositeData>(PassName, out var data))
                {
                    data.Material = _material;
                    data.Source = source;
                    data.History = history;
                    data.HasHistory = history.IsValid();
                    builder.UseTexture(source);
                    if (data.HasHistory) builder.UseTexture(history);
                    builder.SetRenderAttachment(destination, 0);
                    builder.SetRenderFunc(static (CompositeData pass, RasterGraphContext context) =>
                    {
                        if (pass.HasHistory)
                            pass.Material.SetTexture(HistoryId, (RTHandle)pass.History);
                        else
                            pass.Material.SetTexture(HistoryId, Texture2D.blackTexture);
                        Blitter.BlitTexture(context.cmd, pass.Source, new Vector4(1f, 1f, 0f, 0f), pass.Material, CompositePass);
                    });
                }

                resources.cameraColor = destination;
            }

            /// <summary>Two HUD-size textures; made again when the HUD texture size changes.</summary>
            private bool AllocateHistory()
            {
                var size = UnityEngine.Shader.GetGlobalVector(HudLookShaderIds.Size);
                var width = (int)size.x;
                var height = (int)size.y;
                if (width <= 0 || height <= 0) return false;

                var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0) { sRGB = true };
                RenderingUtils.ReAllocateHandleIfNeeded(ref _history[0], descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp,
                    name: "Chopper HUD History A");
                RenderingUtils.ReAllocateHandleIfNeeded(ref _history[1], descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp,
                    name: "Chopper HUD History B");
                return _history[0] != null && _history[1] != null;
            }
        }
    }
}
