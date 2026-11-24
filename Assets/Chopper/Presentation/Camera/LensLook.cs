using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Chopper.Presentation.Camera
{
    /// <summary>
    /// The camera lens (owner, 24 Nov): lens dirt on the bloom and the sun's lens flare, from <see cref="CameraSettings"/>.
    /// Shared by the game camera (<see cref="SpeedEffects"/>) and the effects bench, so both look the same. Its own global
    /// volume sets only the dirt (bloom strength and threshold stay with the other volumes); the flare goes on the scene's
    /// sun unless the sun has its own.
    /// </summary>
    public sealed class LensLook : IDisposable
    {
        private readonly GameObject _volumeObject;
        private readonly VolumeProfile _profile;
        private readonly LensFlareComponentSRP _sunFlare;

        public LensLook(CameraSettings settings, UnityEngine.Camera camera)
        {
            if (settings == null) return;
            if (camera != null)
            {
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
            }
            if (settings.LensDirt != null)
            {
                _volumeObject = new GameObject("Lens Dirt Volume");
                _profile = ScriptableObject.CreateInstance<VolumeProfile>();
                _profile.name = "Lens Dirt (runtime)";
                var bloom = _profile.Add<Bloom>();
                bloom.active = true;
                bloom.dirtTexture.Override(settings.LensDirt);
                bloom.dirtIntensity.Override(settings.LensDirtIntensity);
                var volume = _volumeObject.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 105f;
                volume.sharedProfile = _profile;
            }
            _sunFlare = AddSunFlare(settings);
        }

        private static LensFlareComponentSRP AddSunFlare(CameraSettings settings)
        {
            if (settings.SunFlare == null) return null;
            var sun = RenderSettings.sun;
            if (sun == null)
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.type == LightType.Directional) { sun = light; break; }
            if (sun == null || sun.GetComponent<LensFlareComponentSRP>() != null) return null;
            var flare = sun.gameObject.AddComponent<LensFlareComponentSRP>();
            flare.lensFlareData = settings.SunFlare;
            flare.intensity = settings.SunFlareIntensity;
            flare.useOcclusion = true;
            flare.occlusionRadius = 0.1f;
            flare.allowOffScreen = false;
            return flare;
        }

        public void Dispose()
        {
            if (_volumeObject != null) Object.Destroy(_volumeObject);
            if (_profile != null) Object.Destroy(_profile);
            if (_sunFlare != null) Object.Destroy(_sunFlare);
        }
    }
}
