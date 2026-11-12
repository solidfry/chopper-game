using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Build size (12 Nov 2026): caps the import size of third-party textures, which come in at 4096 with default
    /// settings. Only the import settings (the .meta files) change, never the images, and sizes only go down.
    /// Our own art sets its sizes in its own step (KitAssets, CityAssets, PilotAssets) and is not listed here.
    /// </summary>
    internal static class TextureBudget
    {
        /// <summary>Folders with third-party textures.</summary>
        private static readonly string[] Folders =
        {
            "Assets/UniStorm Weather System",
            "Assets/Textures",
            "Assets/AssetBundles",
        };

        private const int MaxSize = 2048;
        /// <summary>Ambient occlusion and other low-detail data maps.</summary>
        private const int DetailSize = 1024;

        public static void EnsureAll()
        {
            var changed = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var (importer, limit) in OverBudget())
                {
                    importer.maxTextureSize = limit;
                    var standalone = importer.GetPlatformTextureSettings("Standalone");
                    if (standalone.overridden && standalone.maxTextureSize > limit)
                    {
                        standalone.maxTextureSize = limit;
                        importer.SetPlatformTextureSettings(standalone);
                    }
                    importer.SaveAndReimport();
                    changed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            Debug.Log($"Setup: texture budget: {changed} third-party texture(s) capped (max {MaxSize}, detail maps {DetailSize}).");
        }

        /// <summary>For the Validate list.</summary>
        public static IEnumerable<(string Message, Object Context)> Problems()
        {
            var over = OverBudget().ToList();
            if (over.Count > 0)
                yield return ($"{over.Count} third-party texture(s) are over the build size budget. Press 'Texture budget (build size)'.",
                    AssetDatabase.LoadAssetAtPath<Object>(over[0].Importer.assetPath));
        }

        private static IEnumerable<(TextureImporter Importer, int Limit)> OverBudget()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", Folders.Where(AssetDatabase.IsValidFolder).ToArray()))
            {
                if (AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is not TextureImporter importer) continue;
                var limit = Limit(importer.assetPath);
                var standalone = importer.GetPlatformTextureSettings("Standalone");
                if (importer.maxTextureSize > limit || (standalone.overridden && standalone.maxTextureSize > limit))
                    yield return (importer, limit);
            }
        }

        private static int Limit(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var detail = name.EndsWith("_AO", StringComparison.OrdinalIgnoreCase) ||
                         name.EndsWith("_Occlusion", StringComparison.OrdinalIgnoreCase) ||
                         name.IndexOf("AmbientOcclusion", StringComparison.OrdinalIgnoreCase) >= 0;
            return detail ? DetailSize : MaxSize;
        }
    }
}
