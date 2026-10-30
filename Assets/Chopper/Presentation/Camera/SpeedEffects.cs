using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Chopper.Presentation.Camera
{
    /// <summary>
    /// The sense of speed around the chopper camera, owned by <see cref="ChopperCameraRig"/>:
    /// <list type="bullet">
    /// <item>A global post-processing volume made at runtime: bloom always; chromatic aberration grows with speed; boost adds
    /// vignette, saturation and an exposure flash at the kick. No motion blur: camera-only blur smears the chopper,
    /// which moves with the camera. No lens distortion: switching it on and off (the boost spring crosses zero after a
    /// boost) rendered single frames as one flat colour (the pulse and flash bug, 28 Oct).</item>
    /// <item>Speed lines: streaks in camera space that rush past on a ring around the view centre (so they never cover the
    /// chopper or the reticle). More, faster and hotter while boosting.</item>
    /// </list>
    /// Values come from <see cref="CameraSettings"/>, read every frame (tune live in play mode).
    /// </summary>
    public sealed class SpeedEffects : IDisposable
    {
        /// <summary>Lines start this far ahead of the camera and rush back past it.</summary>
        private const float LineDistance = 45f;
        private const float LineRingRadius = 32f;
        /// <summary>Line speed (m/s, camera space) on top of the chopper speed: lines pass faster than the world.</summary>
        private const float LineExtraSpeed = 60f;
        /// <summary>Line length per m/s of line speed.</summary>
        private const float LineStretch = 0.03f;
        /// <summary>How fast the exposure flash fades (1/s).</summary>
        private const float FlashFade = 6f;

        private readonly GameObject _volumeObject;
        private readonly VolumeProfile _profile;
        private readonly Volume _volume;
        private readonly Bloom _bloom;
        private readonly ChromaticAberration _chromatic;
        private readonly Vignette _vignette;
        private readonly ColorAdjustments _colour;

        private readonly UniversalAdditionalCameraData _cameraData;
        private readonly ParticleSystem _lines;
        private readonly Material _lineMaterial;
        private float _flash;

        public SpeedEffects(Transform cameraTransform, UnityEngine.Camera camera, CameraSettings settings)
        {
            // Post effects need post processing on the camera; the volume is global and above scene volumes.
            // Stop NaN: one NaN/Inf pixel from any shader would otherwise spread through bloom and flash the whole screen.
            _cameraData = camera.GetUniversalAdditionalCameraData();
            _cameraData.renderPostProcessing = settings.PostEffects;
            _cameraData.stopNaN = true;

            _volumeObject = new GameObject("Speed Effects Volume");
            _volumeObject.transform.SetParent(cameraTransform, false);
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "Speed Effects (runtime)";
            _bloom = Add<Bloom>();
            _chromatic = Add<ChromaticAberration>();
            _vignette = Add<Vignette>();
            _colour = Add<ColorAdjustments>();
            // Electric Blue - Dark (style guide palette).
            _vignette.color.Override(new Color(0.027f, 0f, 0.098f));
            _vignette.smoothness.Override(0.5f);

            _volume = _volumeObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            _volume.sharedProfile = _profile;

            if (settings.SpeedLineShader != null)
            {
                _lineMaterial = new Material(settings.SpeedLineShader) { name = "Speed Lines (runtime)" };
                _lines = CreateLines(cameraTransform, _lineMaterial);
            }
        }

        /// <param name="speed">Chopper speed, m/s.</param>
        /// <param name="speed01">Speed as a fraction of the full effect speed.</param>
        /// <param name="boost">Boost look, 0..1 (can overshoot a little: the punch).</param>
        /// <param name="kicked">A boost started this frame.</param>
        /// <param name="share">The view's share of the speed and boost effects (<see cref="CameraView.Effects"/>); bloom stays.</param>
        public void Apply(CameraSettings settings, float speed, float speed01, float boost, bool kicked, float deltaTime, float share = 1f)
        {
            // Off = no post processing on the camera at all (not only an empty volume), so it can be ruled out.
            _volume.weight = settings.PostEffects ? 1f : 0f;
            if (_cameraData != null) _cameraData.renderPostProcessing = settings.PostEffects;
            var boost01 = Mathf.Clamp01(boost);

            if (kicked) _flash = 1f;
            _flash = Mathf.Max(0f, _flash - _flash * FlashFade * deltaTime - 0.01f);

            _bloom.intensity.Override(settings.BloomIntensity + 0.6f * boost01);
            _bloom.threshold.Override(settings.BloomThreshold);
            // Clamp: a near-infinite pixel (smooth surface catching the sun) overflows the bloom chain and blacks out or
            // whites out the whole frame. Stop NaN does not catch it: the pixel is not NaN.
            _bloom.clamp.Override(settings.BloomClamp);
            _chromatic.intensity.Override(Mathf.Clamp01((settings.SpeedChromatic * speed01 + settings.BoostChromatic * boost) * share));
            _vignette.intensity.Override(Mathf.Clamp01(settings.BoostVignette * boost * share));
            _colour.postExposure.Override(settings.BoostFlash * _flash * share);
            _colour.saturation.Override(12f * boost01 * share);

            UpdateLines(settings, speed, boost01, share);
        }

        private void UpdateLines(CameraSettings settings, float speed, float boost01, float share)
        {
            if (_lines == null) return;
            var full = Mathf.Max(settings.FullEffectSpeed, settings.SpeedLineStartSpeed + 1f);
            var speedPart = Mathf.Clamp01((speed - settings.SpeedLineStartSpeed) / (full - settings.SpeedLineStartSpeed));
            var emission = _lines.emission;
            emission.rateOverTime = (settings.SpeedLineRate * speedPart + settings.SpeedLineBoostRate * boost01) * share;

            // New lines only: lines in flight keep the speed and colour they started with.
            var lineSpeed = speed + LineExtraSpeed * (0.3f + boost01);
            var main = _lines.main;
            main.startSpeed = -lineSpeed;
            main.startLifetime = (LineDistance + 10f) / Mathf.Max(lineSpeed, 1f);
            // Boost: a random mix of the two colours (cyan and magenta), not one blended colour.
            main.startColor = boost01 > 0.05f
                ? new ParticleSystem.MinMaxGradient(settings.SpeedLineColor, settings.SpeedLineBoostColor)
                : new ParticleSystem.MinMaxGradient(settings.SpeedLineColor);
            if (!_lines.isPlaying) _lines.Play();
        }

        public void Dispose()
        {
            if (_volumeObject != null) UnityEngine.Object.Destroy(_volumeObject);
            if (_profile != null) UnityEngine.Object.Destroy(_profile);
            if (_lines != null) UnityEngine.Object.Destroy(_lines.gameObject);
            if (_lineMaterial != null) UnityEngine.Object.Destroy(_lineMaterial);
        }

        private T Add<T>() where T : VolumeComponent
        {
            var component = _profile.Add<T>(true);
            component.active = true;
            return component;
        }

        private static ParticleSystem CreateLines(Transform cameraTransform, Material material)
        {
            var go = new GameObject("Speed Lines");
            go.transform.SetParent(cameraTransform, false);
            go.transform.localPosition = new Vector3(0f, 0f, LineDistance);
            var lines = go.AddComponent<ParticleSystem>();
            lines.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = lines.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = 0.5f;
            main.startSpeed = -100f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            // Camera space: the lines stay round the view while the chopper turns.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 600;
            main.scalingMode = ParticleSystemScalingMode.Local;

            // A ring (cone of angle 0 = a tube): lines start along the camera's view axis, outside its middle,
            // and move straight back past the camera.
            var shape = lines.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 0f;
            shape.radius = LineRingRadius;
            shape.radiusThickness = 0.4f;
            shape.arc = 360f;

            var fade = lines.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = LineStretch;
            renderer.cameraVelocityScale = 0f;
            renderer.lengthScale = 1f;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return lines;
        }
    }
}
