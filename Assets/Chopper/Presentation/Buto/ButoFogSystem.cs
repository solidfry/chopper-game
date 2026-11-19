using Chopper.Presentation.Bridge;
using Chopper.Simulation.Visuals;
using OccaSoftware.Buto.Runtime;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace Chopper.Presentation.Visuals
{
    /// <summary>
    /// Client only, cosmetic. Volumetric fog near the camera with the Buto package (OccaSoftware): a global Volume with a
    /// <see cref="ButoVolumetricFog"/> override, made at runtime while a city map is loaded. It reads the same data as
    /// the full-screen height fog: the map's <see cref="CityAtmosphere"/> (colour, density, base, falloff, glow) and the
    /// match conditions (<c>_CityFogEnv*</c> globals from <see cref="EnvironmentSystem"/>: time tint and glow, weather
    /// density and colour). It sets <c>_CityFogStart</c> to its range, so the height fog only adds the far part.
    /// City point lights light the fog: each gets a <see cref="ButoLight"/> once (companion lights of the city SubScene),
    /// and every half second only the nearest <c>MaxLights</c> stay on (Buto sorts every on light each frame).
    /// Tuning: <see cref="EnvironmentSettings.Volumetric"/>. Its own assembly (BUTO_PRESENT): the game builds without Buto.
    /// Written without the editor (18 Nov 2026).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(EnvironmentSystem))]
    public partial class ButoFogSystem : PresentationSystemBase
    {
        private static readonly int FogStartId = Shader.PropertyToID("_CityFogStart");
        private static readonly int FogTintId = Shader.PropertyToID("_CityFogEnvTint");
        private static readonly int FogColourId = Shader.PropertyToID("_CityFogEnvColour");
        private static readonly int FogGlowId = Shader.PropertyToID("_CityFogEnvGlow");

        private GameObject _volumeObject;
        private VolumeProfile _profile;
        private ButoVolumetricFog _fog;
        private readonly System.Collections.Generic.List<ButoLight> _lights = new();
        private readonly System.Collections.Generic.List<(float Distance, ButoLight Light)> _sorted = new();
        private float _nextLightPick;
        private bool _lightsFound;
        private bool _masksBuilt;
        private float _nextLightSearch;
        private RenderTexture _emptyShadowmap;
        private static readonly int MainLightShadowmapId = Shader.PropertyToID("_MainLightShadowmapTexture");

        protected override void OnUpdate()
        {
            var look = TryGetContext(out var context) && context.Config != null && context.Config.Environment != null
                ? context.Config.Environment.Volumetric
                : null;
            if (look == null || !look.Enabled || !SystemAPI.TryGetSingleton<CityAtmosphere>(out var atmosphere) || atmosphere.Density <= 0f)
            {
                Off();
                return;
            }

            if (_volumeObject == null) Make();
            Apply(look, atmosphere);
            Shader.SetGlobalFloat(FogStartId, look.Range);
            PickLights(look.MaxLights);
            if (!_masksBuilt && look.LavaFogDensity != 1f && SystemAPI.TryGetSingleton<CityTraffic>(out var traffic) && traffic.Paths.IsCreated)
                BuildLavaMasks(look, ref traffic.Paths.Value.Lava);
        }

        /// <summary>
        /// Thicker fog over lava (owner todo, 18 Nov 2026): a sphere <see cref="FogDensityMask"/> every
        /// <see cref="EnvironmentSettings.VolumetricFogLook.LavaFogSpacing"/> m along each lava line (Buto uses the 8
        /// nearest the camera). Children of the fog volume object, so <see cref="Off"/> removes them.
        /// </summary>
        private void BuildLavaMasks(EnvironmentSettings.VolumetricFogLook look, ref Unity.Entities.BlobArray<LavaPath> lava)
        {
            _masksBuilt = true;
            var line = new System.Collections.Generic.List<Unity.Mathematics.float3>();
            var points = new System.Collections.Generic.List<Unity.Mathematics.float3>();
            var count = 0;
            for (var p = 0; p < lava.Length; p++)
            {
                ref var path = ref lava[p];
                line.Clear();
                points.Clear();
                for (var i = 0; i < path.Points.Length; i++) line.Add(path.Points[i]);
                LavaFogPlacement.Points(line, look.LavaFogSpacing, points);
                var width = Mathf.Max(5f, path.Width * look.LavaFogWidthScale);
                foreach (var point in points)
                {
                    var go = new GameObject("Lava Fog Mask");
                    go.transform.SetParent(_volumeObject.transform, false);
                    go.transform.position = (Vector3)point + Vector3.up * (look.LavaFogHeight * 0.3f);
                    go.transform.localScale = new Vector3(width, look.LavaFogHeight, width);
                    var mask = go.AddComponent<FogDensityMask>();
                    mask.DensityMultiplier = look.LavaFogDensity;
                    // Blend distance has no setter: set the serialized field.
                    JsonUtility.FromJsonOverwrite($"{{\"blendDistance\":{look.LavaFogBlend.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}", mask);
                    count++;
                }
            }
            Debug.Log($"[Fog] {count} lava fog masks on {lava.Length} lava lines.");
        }

        protected override void OnDestroy()
        {
            Off();
            if (_emptyShadowmap != null)
            {
                _emptyShadowmap.Release();
                Object.Destroy(_emptyShadowmap);
                _emptyShadowmap = null;
            }
            base.OnDestroy();
        }

        private void Make()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "Chopper Buto Fog (runtime)";
            _fog = _profile.Add<ButoVolumetricFog>(true);
            _volumeObject = new GameObject("Volumetric Fog (Buto)");
            var volume = _volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50f;
            volume.sharedProfile = _profile;

            // Buto's LightingSampler compute reads the main light shadow map. When URP draws no main light shadows
            // (quality level or light without shadows; seen 18 Nov 2026 in the Windows build), nothing binds it and the
            // dispatch logs "Property (_MainLightShadowmapTexture) ... is not set" every frame. A 1x1 shadow map as the
            // global fallback: URP's own binding replaces it whenever it draws shadows.
            if (_emptyShadowmap == null)
            {
                _emptyShadowmap = new RenderTexture(1, 1, 16, RenderTextureFormat.Shadowmap) { name = "Chopper Buto empty shadowmap" };
                _emptyShadowmap.Create();
            }
            Shader.SetGlobalTexture(MainLightShadowmapId, _emptyShadowmap);
        }

        private void Apply(EnvironmentSettings.VolumetricFogLook look, in CityAtmosphere atmosphere)
        {
            // Conditions (EnvironmentSystem); while unset, plain values.
            var tint = Shader.GetGlobalVector(FogTintId);
            var weather = Shader.GetGlobalVector(FogColourId);
            var glow = Shader.GetGlobalVector(FogGlowId);
            var set = glow.y > 0.5f;
            var densityScale = set ? tint.w : 1f;
            var tintColour = set ? new Color(tint.x, tint.y, tint.z) : Color.white;
            var glowAmount = set ? glow.x : 1f;

            var baseColour = new Color(atmosphere.Colour.x, atmosphere.Colour.y, atmosphere.Colour.z) * tintColour;
            if (set) baseColour = Color.Lerp(baseColour, new Color(weather.x, weather.y, weather.z), weather.w);
            var glowColour = new Color(atmosphere.Glow.x, atmosphere.Glow.y, atmosphere.Glow.z);

            _fog.mode.Override(VolumetricFogMode.On);
            _fog.maxDistanceVolumetric.Override(look.Range);
            _fog.depthRatio.Override(look.DepthRatio);
            _fog.gridPixelSize.Override(look.GridPixelSize);
            _fog.gridSizeZ.Override(look.GridSlices);
            // Buto's density is x 0.01 per metre; the map's is per metre.
            _fog.fogDensity.Override(atmosphere.Density * 100f * look.DensityScale * densityScale);
            _fog.baseHeight.Override(atmosphere.BaseHeight);
            _fog.attenuationBoundarySize.Override(atmosphere.Falloff);
            _fog.anisotropy.Override(look.Anisotropy);
            _fog.lightIntensity.Override(look.LightIntensity);
            _fog.litColor.Override(baseColour);
            _fog.shadowedColor.Override(baseColour * look.ShadowShade);
            _fog.emitColor.Override(glowColour * (look.GlowEmission * glowAmount));
        }

        /// <summary>Gives every city point light a ButoLight once, then keeps only the nearest <paramref name="most"/> on.</summary>
        private void PickLights(int most)
        {
            // The city lights stream in with the SubScene, maybe after the fog data: look again every 2 s until found.
            if (!_lightsFound && UnityEngine.Time.time >= _nextLightSearch)
            {
                _nextLightSearch = UnityEngine.Time.time + 2f;
                // City lights are companion objects of the SubScene (hidden, so FindObjectsByType misses them). The
                // managed component API is deprecated in 6.6; FindObjectsOfTypeAll sees hidden objects too. Skip assets
                // (no valid scene) and switched-off lights.
                foreach (var light in Resources.FindObjectsOfTypeAll<Light>())
                {
                    if (light == null || !light.gameObject.scene.IsValid() || !light.isActiveAndEnabled) continue;
                    if (light.type != LightType.Point && light.type != LightType.Spot) continue;
                    var buto = light.GetComponent<ButoLight>();
                    if (buto == null) buto = light.gameObject.AddComponent<ButoLight>();
                    buto.enabled = false;
                    _lights.Add(buto);
                }
                _lightsFound = _lights.Count > 0;
            }
            if (UnityEngine.Time.time < _nextLightPick) return;
            _nextLightPick = UnityEngine.Time.time + 0.5f;
            var camera = UnityEngine.Camera.main;
            if (camera == null) return;
            var eye = camera.transform.position;
            _sorted.Clear();
            for (var i = _lights.Count - 1; i >= 0; i--)
            {
                if (_lights[i] == null) { _lights.RemoveAt(i); continue; }
                _sorted.Add(((_lights[i].transform.position - eye).sqrMagnitude, _lights[i]));
            }
            _sorted.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            for (var i = 0; i < _sorted.Count; i++)
            {
                var on = i < most;
                if (_sorted[i].Light.enabled != on) _sorted[i].Light.enabled = on;
            }
        }

        private void Off()
        {
            foreach (var light in _lights)
                if (light != null) light.enabled = false;
            _lights.Clear();
            _lightsFound = false;
            _nextLightSearch = 0f;
            _masksBuilt = false;
            if (_volumeObject != null) Object.Destroy(_volumeObject);
            if (_profile != null) Object.Destroy(_profile);
            _volumeObject = null;
            _profile = null;
            _fog = null;
            Shader.SetGlobalFloat(FogStartId, 0f);
        }
    }
}
