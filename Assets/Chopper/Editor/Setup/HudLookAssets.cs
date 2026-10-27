using System.Collections.Generic;
using System.Linq;
using Chopper.Presentation.Hud;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Retro HUD look: the <see cref="HudLookSettings"/> asset, and the <see cref="HudLookRendererFeature"/> on every URP
    /// renderer in the project (each quality level has its own). Creates only what is missing.
    /// </summary>
    internal static class HudLookAssets
    {
        private const string SettingsPath = "Assets/Chopper/Data/Hud/HudLookSettings.asset";
        private const string ShaderPath = "Assets/Chopper/Presentation/Hud/HudLook.shader";
        private const string FeatureName = "Chopper HUD Look";

        public static HudLookSettings Ensure()
        {
            EnsureRendererFeatures();

            var settings = AssetDatabase.LoadAssetAtPath<HudLookSettings>(SettingsPath);
            if (settings != null) return settings;

            settings = ScriptableObject.CreateInstance<HudLookSettings>();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath));
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        /// <summary>URP renderers under Assets (the project's quality levels point to these).</summary>
        public static IEnumerable<UniversalRendererData> Renderers() => AssetDatabase
            .FindAssets("t:UniversalRendererData", new[] { "Assets" })
            .Select(guid => AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(r => r != null);

        public static HudLookRendererFeature FindFeature(ScriptableRendererData renderer) =>
            renderer.rendererFeatures.OfType<HudLookRendererFeature>().FirstOrDefault();

        private static void EnsureRendererFeatures()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) Debug.LogError($"Setup: no HUD look shader at {ShaderPath}.");

            foreach (var renderer in Renderers())
            {
                var feature = FindFeature(renderer);
                if (feature == null) feature = AddFeature(renderer);
                if (feature.Shader == null && shader != null)
                {
                    feature.SetShader(shader);
                    EditorUtility.SetDirty(feature);
                }
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Same steps as URP's renderer inspector: sub-asset, feature list and feature map.</summary>
        private static HudLookRendererFeature AddFeature(ScriptableRendererData renderer)
        {
            var feature = ScriptableObject.CreateInstance<HudLookRendererFeature>();
            feature.name = FeatureName;
            AssetDatabase.AddObjectToAsset(feature, renderer);

            var serialized = new SerializedObject(renderer);
            var features = serialized.FindProperty("m_RendererFeatures");
            var map = serialized.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId))
            {
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            }
            serialized.ApplyModifiedProperties();

            renderer.SetDirty();
            EditorUtility.SetDirty(renderer);
            return feature;
        }
    }
}
