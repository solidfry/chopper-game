using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// The nuke blast on the whole screen (owner, 24 Nov: "a full screen effect as well"), one per blast, owned by
    /// <see cref="NukeBlastPlayer"/>. A global post-processing volume made at runtime, above the speed effects volume:
    /// an exposure flash at the detonation, a warm, hot tint and a dark red vignette while the fireball burns, and a
    /// chromatic split when the shock front passes the camera. Faded by the volume weight; no component is switched
    /// on and off (switching lens distortion made one-frame flat colour flashes, see SpeedEffects), so no lens warp.
    /// </summary>
    public sealed class NukeScreenFx : IDisposable
    {
        private static readonly Color Warm = new(1f, 0.8f, 0.58f);
        private static readonly Color Ember = new(0.35f, 0.04f, 0f);

        private readonly GameObject _volumeObject;
        private readonly VolumeProfile _profile;
        private readonly Volume _volume;
        private readonly ColorAdjustments _colour;
        private readonly ChromaticAberration _chromatic;
        private readonly Vignette _vignette;

        public NukeScreenFx()
        {
            _volumeObject = new GameObject("Nuke Screen Volume");
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "Nuke Screen (runtime)";
            _colour = _profile.Add<ColorAdjustments>(true);
            _chromatic = _profile.Add<ChromaticAberration>(true);
            _vignette = _profile.Add<Vignette>(true);
            _vignette.color.Override(Ember);
            _vignette.smoothness.Override(0.6f);
            _volume = _volumeObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 110f;
            _volume.weight = 0f;
            _volume.sharedProfile = _profile;
        }

        /// <param name="flash">0..1 white-out (exposure up).</param>
        /// <param name="heat">0..1 the fireball's heat on the view (tint, vignette).</param>
        /// <param name="shock">0..1 the shock front passing (chromatic split).</param>
        public void Tick(float flash, float heat, float shock)
        {
            var weight = Mathf.Clamp01(Mathf.Max(flash, Mathf.Max(heat, shock)));
            _volume.weight = weight;
            if (weight <= 0f) return;
            // Values are for full weight; divide so the blend gives the wanted amount of each.
            var k = 1f / weight;
            _colour.postExposure.Override(3f * flash * k);
            _colour.colorFilter.Override(Color.Lerp(Color.white, Warm, Mathf.Clamp01(heat * k)));
            _colour.saturation.Override(25f * heat * k);
            _colour.contrast.Override(15f * heat * k);
            _chromatic.intensity.Override(Mathf.Clamp01((0.9f * shock + 0.35f * flash) * k));
            _vignette.intensity.Override(Mathf.Clamp01(0.4f * heat * k));
        }

        public void Dispose()
        {
            if (_volumeObject != null) Object.Destroy(_volumeObject);
            if (_profile != null) Object.Destroy(_profile);
        }
    }
}
