using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Chopper.Presentation.Camera
{
    /// <summary>
    /// Draws the special view of the optics (<see cref="ZoomState"/>), owned by <see cref="ChopperCameraRig"/>:
    /// <list type="bullet">
    /// <item>A global post-processing volume made at runtime, above the speed effects' volume: its weight is the view's
    /// blend, so the picture fades between normal and the mode's <see cref="VisionLook"/>.</item>
    /// <item>Glows: one soft quad per marked target, turned to the camera and drawn before the grade, so the grade
    /// colours them too. A pool; no objects are made while flying after the first use.</item>
    /// </list>
    /// With post effects off (camera settings) only the glows show.
    /// </summary>
    public sealed class VisionEffects : IDisposable
    {
        /// <summary>The glow sits this far towards the camera, so the target's own hull does not cut it in half.</summary>
        private const float TowardsCamera = 3f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");

        private readonly Transform _camera;
        private readonly GameObject _volumeObject;
        private readonly VolumeProfile _profile;
        private readonly Volume _volume;
        private readonly ColorAdjustments _colour;
        private readonly ChannelMixer _mixer;
        private readonly FilmGrain _grain;
        private readonly Vignette _vignette;

        private readonly Transform _glowRoot;
        private readonly Material _glowMaterial;
        private readonly Mesh _quad;
        private readonly List<MeshRenderer> _glows = new();
        private readonly MaterialPropertyBlock _block = new();
        private int _shownGlows;

        public VisionEffects(Transform cameraTransform, CameraSettings settings)
        {
            _camera = cameraTransform;
            _volumeObject = new GameObject("Vision Volume");
            _volumeObject.transform.SetParent(cameraTransform, false);
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "Vision (runtime)";
            _colour = Add<ColorAdjustments>();
            _mixer = Add<ChannelMixer>();
            _grain = Add<FilmGrain>();
            _vignette = Add<Vignette>();
            _grain.type.Override(FilmGrainLookup.Medium3);
            _grain.response.Override(0.6f);
            _vignette.color.Override(Color.black);
            _vignette.smoothness.Override(0.45f);

            _volume = _volumeObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            // Above the speed effects (100): the view's exposure and vignette win while it is on.
            _volume.priority = 110f;
            _volume.weight = 0f;
            _volume.sharedProfile = _profile;

            if (settings.VisionGlowShader == null) return;
            _glowMaterial = new Material(settings.VisionGlowShader) { name = "Vision Glow (runtime)" };
            _quad = CreateQuad();
            // World space, not under the camera: the glows sit on the targets.
            _glowRoot = new GameObject("Vision Glows").transform;
        }

        /// <param name="look">The mode's look; null = no special view.</param>
        /// <param name="blend">0 = normal picture, 1 = the full view.</param>
        /// <param name="glows">Marked targets of this frame (may be empty).</param>
        public void Apply(CameraSettings settings, VisionLook look, float blend, IReadOnlyList<VisionGlow> glows)
        {
            var on = look != null && blend > 0f;
            _volume.weight = on && settings.PostEffects ? Mathf.Clamp01(blend) : 0f;
            if (!on)
            {
                ShowGlows(0);
                return;
            }

            _colour.postExposure.Override(look.Exposure);
            _colour.contrast.Override(look.Contrast);
            SetMix(look.RedMix, _mixer.redOutRedIn, _mixer.redOutGreenIn, _mixer.redOutBlueIn);
            SetMix(look.GreenMix, _mixer.greenOutRedIn, _mixer.greenOutGreenIn, _mixer.greenOutBlueIn);
            SetMix(look.BlueMix, _mixer.blueOutRedIn, _mixer.blueOutGreenIn, _mixer.blueOutBlueIn);
            _grain.intensity.Override(look.Grain);
            _vignette.intensity.Override(look.Vignette);

            DrawGlows(look, blend, glows);
        }

        /// <summary>The channel mixer counts in percent.</summary>
        private static void SetMix(Vector3 mix, ClampedFloatParameter red, ClampedFloatParameter green, ClampedFloatParameter blue)
        {
            red.Override(mix.x * 100f);
            green.Override(mix.y * 100f);
            blue.Override(mix.z * 100f);
        }

        private void DrawGlows(VisionLook look, float blend, IReadOnlyList<VisionGlow> glows)
        {
            if (_glowMaterial == null || glows == null || look.GlowColor.maxColorComponent <= 0f)
            {
                ShowGlows(0);
                return;
            }

            _glowMaterial.SetFloat(ZTestId, (float)(look.ThroughWalls ? CompareFunction.Always : CompareFunction.LessEqual));
            var eye = _camera.position;
            var facing = _camera.rotation;
            var shown = 0;
            for (var i = 0; i < glows.Count; i++)
            {
                var glow = glows[i];
                if (glow.Strength <= 0f) continue;
                var renderer = GlowAt(shown++);
                var toEye = eye - glow.Position;
                var distance = toEye.magnitude;
                var glowTransform = renderer.transform;
                glowTransform.SetPositionAndRotation(
                    distance > 1e-3f ? glow.Position + toEye / distance * Mathf.Min(TowardsCamera, distance * 0.5f) : glow.Position, facing);
                var size = look.GlowSizeAt(distance);
                glowTransform.localScale = new Vector3(size, size, size);
                _block.SetColor(ColorId, look.GlowColor * (glow.Strength * blend));
                renderer.SetPropertyBlock(_block);
            }
            ShowGlows(shown);
        }

        private MeshRenderer GlowAt(int index)
        {
            while (_glows.Count <= index)
            {
                var go = new GameObject("Glow");
                go.transform.SetParent(_glowRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = _quad;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _glowMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;
                _glows.Add(renderer);
            }
            _glows[index].enabled = true;
            return _glows[index];
        }

        /// <summary>Hides the pooled glows after the first <paramref name="count"/>.</summary>
        private void ShowGlows(int count)
        {
            for (var i = count; i < _shownGlows && i < _glows.Count; i++) _glows[i].enabled = false;
            _shownGlows = count;
        }

        public void Dispose()
        {
            if (_volumeObject != null) UnityEngine.Object.Destroy(_volumeObject);
            if (_profile != null) UnityEngine.Object.Destroy(_profile);
            if (_glowRoot != null) UnityEngine.Object.Destroy(_glowRoot.gameObject);
            if (_glowMaterial != null) UnityEngine.Object.Destroy(_glowMaterial);
            if (_quad != null) UnityEngine.Object.Destroy(_quad);
        }

        private T Add<T>() where T : VolumeComponent
        {
            var component = _profile.Add<T>(true);
            component.active = true;
            return component;
        }

        /// <summary>A 1 m quad in the XY plane, centred.</summary>
        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "Vision Glow Quad" };
            mesh.SetVertices(new List<Vector3>
            {
                new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f),
            });
            mesh.SetUVs(0, new List<Vector2> { new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f) });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
