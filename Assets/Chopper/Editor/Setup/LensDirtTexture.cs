using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// The lens dirt texture for bloom (owner, 24 Nov: "some subtle lens dirt"): LensDirt00 from Unity's Post Processing
    /// v2 (github.com/Unity-Technologies/PostProcessing, v2 branch, PostProcessing/Textures/Lens Dirt; Unity Companion
    /// License). Imported at 2048 wide, no mipmaps, clamped. Bloom shows it only where bright light falls.
    /// </summary>
    internal static class LensDirtTexture
    {
        public const string Path = "Assets/Chopper/Art/Effects/LensDirt00.png";

        public static Texture2D Ensure()
        {
            if (AssetImporter.GetAtPath(Path) is TextureImporter importer &&
                (importer.maxTextureSize != 2048 || importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp))
            {
                importer.maxTextureSize = 2048;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Path);
            if (texture == null) Debug.LogWarning($"Lens dirt: {Path} is missing.");
            return texture;
        }
    }
}
