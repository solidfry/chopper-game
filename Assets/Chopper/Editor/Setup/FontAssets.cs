using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Centred Karnivore font assets for UI Toolkit. The Karnivore fonts are capitals only, but their line box keeps room
    /// for tall ascenders (Bold: ascent 1099, descent -145, capitals 0..750 per 1000 units), so capitals sit about
    /// 0.1 em low in every button, pill and label (owner, 19 Nov 2026). Each asset here is a dynamic font asset of the
    /// original .ttf (left unchanged) with the same line height, but the ascent and descent lines moved so the
    /// capitals sit in the middle. The USS uses them with <c>-unity-font-definition</c>. Creates only what is missing.
    /// </summary>
    internal static class FontAssets
    {
        private const string SourceFolder = "Assets/Fonts/Karnivore/";
        private const string TargetFolder = "Assets/Chopper/UI/Fonts/";

        /// <summary>The fonts the UI uses: file name and capital height per em (measured from the 'H' glyph).</summary>
        private static readonly (string File, float CapPerEm)[] Fonts =
        {
            ("KARNIVOL", 0.702f), // Lite: labels, values
            ("KARNIVOB", 0.750f), // Bold: titles, buttons
            ("KARNIVOD", 0.740f), // Dot: display headings, nav
            ("KARNIVSV", 0.750f), // Seven: small decoration
        };

        public static string PathOf(string file) => TargetFolder + file + " Centred.asset";

        public static void EnsureAll()
        {
            foreach (var (file, capPerEm) in Fonts) Ensure(file, capPerEm);
            AssetDatabase.SaveAssets();
        }

        private static void Ensure(string file, float capPerEm)
        {
            var path = PathOf(file);
            if (AssetDatabase.LoadAssetAtPath<FontAsset>(path) != null) return;
            var font = AssetDatabase.LoadAssetAtPath<Font>(SourceFolder + file + ".ttf");
            if (font == null)
            {
                Debug.LogWarning($"[Setup] Font {file}.ttf not found in {SourceFolder}; centred font asset not made.");
                return;
            }

            var asset = FontAsset.CreateFontAsset(font);
            asset.name = file + " Centred";
            var face = asset.faceInfo;
            var height = face.ascentLine - face.descentLine;
            var cap = face.capLine > 0f ? face.capLine : capPerEm * face.pointSize;
            face.capLine = cap;
            face.ascentLine = cap * 0.5f + height * 0.5f;
            face.descentLine = cap * 0.5f - height * 0.5f;
            asset.faceInfo = face;

            System.IO.Directory.CreateDirectory(TargetFolder);
            AssetDatabase.CreateAsset(asset, path);
            // A dynamic font asset keeps its atlas texture and material as sub-assets.
            if (asset.atlasTextures != null)
                foreach (var texture in asset.atlasTextures)
                    if (texture != null && !AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, asset);
            if (asset.material != null && !AssetDatabase.Contains(asset.material)) AssetDatabase.AddObjectToAsset(asset.material, asset);
            EditorUtility.SetDirty(asset);
            Debug.Log($"[Setup] Made {path} (capitals centred: ascent {face.ascentLine:0.#}, descent {face.descentLine:0.#}, cap {cap:0.#}).");
        }
    }
}
