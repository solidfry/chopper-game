using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Part kits made by the PartForge generator (<c>Tools/PartForge</c>, Blender): each kit folder under
    /// <see cref="KitsFolder"/> has a <c>*.manifest.json</c>, FBX models and three palette maps. This step sets the texture
    /// import settings, makes the one URP Lit kit material, and remaps every model's material to it.
    /// Creates only what is missing. Linking kit models to chassis and modules is a later step (plan section 5).
    /// </summary>
    internal static class KitAssets
    {
        public const string KitsFolder = "Assets/Chopper/Art/Kits";

        [Serializable]
        private sealed class Manifest
        {
            public string kit;
            public MaterialInfo material;
            public PartInfo[] parts;
        }

        [Serializable]
        private sealed class MaterialInfo
        {
            public string name;
            public string colour;
            public string emission;
            public string metalSmooth;
        }

        [Serializable]
        private sealed class PartInfo
        {
            public string name;
            public string file;
            public string kind;
            public string slot;
            public int[] moduleIds;
            public int triangles;
        }

        public static void EnsureAll()
        {
            foreach (var path in ManifestPaths()) Ensure(path);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Problems for the Validate list: a kit without its material, or a model not remapped to it.</summary>
        public static IEnumerable<(string Message, Object Context)> Problems()
        {
            foreach (var path in ManifestPaths())
            {
                var manifest = Load(path, out var folder);
                if (manifest?.material == null) { yield return ($"{path}: cannot read the kit manifest.", null); continue; }
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(folder, manifest));
                if (material == null) { yield return ($"Kit {manifest.kit}: no kit material. Press '5. Import part kits'.", null); continue; }
                foreach (var part in manifest.parts ?? Array.Empty<PartInfo>())
                {
                    if (AssetImporter.GetAtPath($"{folder}/{part.file}") is not ModelImporter importer)
                        yield return ($"Kit {manifest.kit}: model {part.file} is missing.", null);
                    else if (!importer.GetExternalObjectMap().ContainsValue(material))
                        yield return ($"Kit {manifest.kit}: {part.name} does not use the kit material. Press '5. Import part kits'.", importer);
                }
            }
        }

        private static IEnumerable<string> ManifestPaths() => Directory.Exists(KitsFolder)
            ? Directory.GetFiles(KitsFolder, "*.manifest.json", SearchOption.AllDirectories)
            : Array.Empty<string>();

        private static Manifest Load(string path, out string folder)
        {
            folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            return JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
        }

        private static string MaterialPath(string folder, Manifest manifest) => $"{folder}/Materials/{manifest.material.name}.mat";

        private static void Ensure(string manifestPath)
        {
            var manifest = Load(manifestPath, out var folder);
            if (manifest?.material == null)
            {
                Debug.LogWarning($"Setup: cannot read kit manifest {manifestPath}.");
                return;
            }

            var colour = Texture($"{folder}/Textures/{manifest.material.colour}", srgb: true);
            var emission = Texture($"{folder}/Textures/{manifest.material.emission}", srgb: true);
            var metalSmooth = Texture($"{folder}/Textures/{manifest.material.metalSmooth}", srgb: false);
            var material = EnsureMaterial(MaterialPath(folder, manifest), colour, emission, metalSmooth);
            if (material == null) return;

            foreach (var part in manifest.parts ?? Array.Empty<PartInfo>())
            {
                if (AssetImporter.GetAtPath($"{folder}/{part.file}") is not ModelImporter importer)
                {
                    Debug.LogWarning($"Setup: kit {manifest.kit} model {part.file} not found.");
                    continue;
                }

                var changed = false;
                // Rigid props: no animation, cameras, lights or colliders (colliders come from the chassis prefab).
                if (importer.importAnimation) { importer.importAnimation = false; changed = true; }
                if (importer.importCameras) { importer.importCameras = false; changed = true; }
                if (importer.importLights) { importer.importLights = false; changed = true; }
                if (importer.addCollider) { importer.addCollider = false; changed = true; }

                var source = new AssetImporter.SourceAssetIdentifier(typeof(Material), manifest.material.name);
                if (!importer.GetExternalObjectMap().TryGetValue(source, out var mapped) || mapped != material)
                {
                    importer.AddRemap(source, material);
                    changed = true;
                }
                if (changed) importer.SaveAndReimport();
            }
        }

        /// <summary>Palette maps: point filter (each face samples one swatch), clamp, no compression artefacts between cells.</summary>
        private static Texture2D Texture(string path, bool srgb)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                (importer.sRGBTexture != srgb || importer.filterMode != FilterMode.Point ||
                 importer.textureCompression != TextureImporterCompression.Uncompressed || importer.mipmapEnabled))
            {
                importer.sRGBTexture = srgb;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) Debug.LogWarning($"Setup: kit texture {path} not found.");
            return texture;
        }

        private static Material EnsureMaterial(string path, Texture2D colour, Texture2D emission, Texture2D metalSmooth)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("Setup: URP Lit shader not found.");
                return null;
            }

            var material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            material.SetTexture("_BaseMap", colour);
            material.SetColor("_BaseColor", Color.white);
            // Metallic in R, smoothness in A (URP's metallic map layout).
            material.SetTexture("_MetallicGlossMap", metalSmooth);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Smoothness", 1f);
            // Neon swatches glow; bloom picks them up.
            material.SetTexture("_EmissionMap", emission);
            material.SetColor("_EmissionColor", Color.white * 3f);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
