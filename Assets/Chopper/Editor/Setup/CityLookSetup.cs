using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Setup step 9, the city look (plan: light and air). On every URP renderer in use: the full-screen height fog pass
    /// (<c>Art/Shaders/CityHeightFog.shader</c>, before post processing, depth) and screen-space ambient occlusion. A
    /// post profile (<c>Data/Rendering/CityPost.asset</c>: bloom on the lights, ACES tonemapping, a little contrast and
    /// a vignette) on a Global Volume in each open match scene with a city SubScene, and post processing on its cameras. Each map's fog numbers come
    /// with the city scene (step 8). Made once, then left alone, so tuning by hand stays.
    /// Written without the editor (4 Nov 2026): check the renderer asset after the first run.
    /// </summary>
    internal static class CityLookSetup
    {
        public const string FogShader = "Hidden/Chopper/CityHeightFog";
        private const string Folder = "Assets/Chopper/Data/Rendering";
        private const string FogMaterialPath = Folder + "/CityHeightFog.mat";
        private const string ProfilePath = Folder + "/CityPost.asset";
        private const string VolumeName = "City Post Volume";
        // Internal in some URP versions: found by name.
        private const string SsaoType = "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion, Unity.RenderPipelines.Universal.Runtime";

        public static void Apply()
        {
            var renderers = RendererDatas();
            if (renderers.Count == 0)
                Debug.LogWarning("Setup: no URP renderer found (Project Settings > Graphics / Quality). No city fog pass or SSAO.");
            var fog = EnsureFogMaterial();
            foreach (var data in renderers)
            {
                if (fog != null) EnsureFog(data, fog);
                EnsureSsao(data);
            }
            var profile = EnsureProfile();
            EnsureVolume(profile);
            AssetDatabase.SaveAssets();
        }

        public static IEnumerable<(string Message, Object Context)> Problems()
        {
            if (Shader.Find(FogShader) == null)
                yield return ($"No '{FogShader}' shader (Art/Shaders/CityHeightFog.shader does not compile?).", null);
            foreach (var data in RendererDatas())
                if (!data.rendererFeatures.OfType<FullScreenPassRendererFeature>().Any(f => f != null && f.passMaterial != null &&
                                                                                       f.passMaterial.shader.name == FogShader))
                    yield return ($"Renderer {data.name}: no city height fog pass. Press '9. City look'.", data);
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath) == null)
                yield return ("No city post profile. Press '9. City look'.", null);
        }

        /// <summary>The renderer data of every URP asset in use (default and per quality level).</summary>
        private static List<ScriptableRendererData> RendererDatas()
        {
            var assets = new HashSet<UniversalRenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset main) assets.Add(main);
            for (var i = 0; i < QualitySettings.names.Length; i++)
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset level) assets.Add(level);
            var list = new List<ScriptableRendererData>();
            foreach (var asset in assets)
            {
                var serialized = new SerializedObject(asset);
                var array = serialized.FindProperty("m_RendererDataList");
                if (array == null) continue;
                for (var i = 0; i < array.arraySize; i++)
                    if (array.GetArrayElementAtIndex(i).objectReferenceValue is ScriptableRendererData data && !list.Contains(data))
                        list.Add(data);
            }
            return list;
        }

        private static Material EnsureFogMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(FogMaterialPath);
            if (material != null) return material;
            var shader = Shader.Find(FogShader);
            if (shader == null)
            {
                Debug.LogWarning($"Setup: no '{FogShader}' shader yet (does Art/Shaders/CityHeightFog.shader compile?). No city fog.");
                return null;
            }
            Directory.CreateDirectory(Folder);
            material = new Material(shader) { name = "CityHeightFog" };
            AssetDatabase.CreateAsset(material, FogMaterialPath);
            return material;
        }

        private static void EnsureFog(ScriptableRendererData data, Material fog)
        {
            if (data.rendererFeatures.OfType<FullScreenPassRendererFeature>().Any(f => f != null && f.passMaterial == fog)) return;
            var feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = "City Height Fog";
            feature.passMaterial = fog;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            feature.fetchColorBuffer = true;
            feature.requirements = ScriptableRenderPassInput.Depth;
            AddFeature(data, feature);
        }

        private static void EnsureSsao(ScriptableRendererData data)
        {
            var type = Type.GetType(SsaoType);
            if (type == null)
            {
                Debug.LogWarning("Setup: URP Screen Space Ambient Occlusion feature type not found; add it to the renderer by hand.");
                return;
            }
            if (data.rendererFeatures.Any(f => f != null && f.GetType() == type)) return;
            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
            feature.name = "Screen Space Ambient Occlusion";
            AddFeature(data, feature);
        }

        /// <summary>Adds a feature the way the renderer inspector does: a sub-asset, in the feature list and the id map.</summary>
        private static void AddFeature(ScriptableRendererData data, ScriptableRendererFeature feature)
        {
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.SaveAssets();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
            var serialized = new SerializedObject(data);
            serialized.Update();
            var features = serialized.FindProperty("m_RendererFeatures");
            var map = serialized.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            if (map != null)
            {
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            }
            serialized.ApplyModifiedProperties();
            data.SetDirty();
            EditorUtility.SetDirty(data);
            Debug.Log($"Setup: added '{feature.name}' to renderer {data.name}.");
        }

        internal static VolumeProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile != null) return profile;
            Directory.CreateDirectory(Folder);
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.72f);
            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);
            var colour = profile.Add<ColorAdjustments>(true);
            colour.contrast.Override(10f);
            colour.saturation.Override(-4f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.45f);
            // Volume components are sub-assets of the profile.
            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>
        /// A Global Volume with the city profile in each open match scene that holds a city SubScene (a Volume inside
        /// the SubScene would be baked away), and post processing on that scene's cameras. Other scenes (hangar, menus)
        /// keep their own look.
        /// </summary>
        internal static void EnsureVolume(VolumeProfile profile)
        {
            var hosts = GameplaySceneSetup.SubScenesInOpenScenes()
                .Where(s => s.Path.StartsWith(CityAssets.SceneFolder + "/City", StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Host).Distinct().ToArray();
            if (hosts.Length == 0)
            {
                Debug.Log("Setup: no open scene has a city SubScene, so no city post volume was added. Open the match " +
                          "scene with the city SubScene and press '9. City look' again.");
                return;
            }
            foreach (var scene in hosts)
            {
                var roots = scene.GetRootGameObjects();
                var volume = roots.SelectMany(r => r.GetComponentsInChildren<Volume>(true))
                    .FirstOrDefault(v => v.isGlobal && v.sharedProfile == profile);
                if (volume == null)
                {
                    var go = new GameObject(VolumeName);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    Undo.RegisterCreatedObjectUndo(go, "Add city post volume");
                    volume = Undo.AddComponent<Volume>(go);
                    volume.isGlobal = true;
                    volume.sharedProfile = profile;
                }
                foreach (var camera in roots.SelectMany(r => r.GetComponentsInChildren<UnityEngine.Camera>(true)))
                {
                    var data = camera.GetComponent<UniversalAdditionalCameraData>();
                    if (data == null) data = Undo.AddComponent<UniversalAdditionalCameraData>(camera.gameObject);
                    Undo.RecordObject(data, "City post processing");
                    data.renderPostProcessing = true;
                    EditorUtility.SetDirty(data);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log($"Setup: city post volume ready in scene '{scene.name}'.");
            }
        }
    }
}
