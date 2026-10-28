using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chopper.Presentation.Visuals;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Part kits made by the PartForge generator (<c>Tools/PartForge</c>, Blender): each kit folder under
    /// <see cref="KitsFolder"/> has a <c>*.manifest.json</c>, FBX models and baked texture sets (one per chassis, one
    /// shared by the modules), each painted in every skin. This step sets the texture import settings, makes one URP Lit
    /// material per texture set and skin, remaps every model to its set's default-skin material, and fills the kit's
    /// <see cref="SkinLibrary"/> (the default kit's library goes into the game config).
    /// Creates only what is missing (materials are left alone once made). Linking kit models to chassis and modules is a
    /// later step (plan section 5).
    /// </summary>
    internal static class KitAssets
    {
        public const string KitsFolder = "Assets/Chopper/Art/Kits";
        /// <summary>The kit whose skins the game uses (PartForge's default style, "dystopia").</summary>
        public const string DefaultKit = "Kit02";

        [Serializable]
        private sealed class Manifest
        {
            public string kit;
            public TextureSetInfo[] textureSets;
            public SkinInfo[] skins;
            public PartInfo[] parts;
        }

        [Serializable]
        private sealed class TextureSetInfo
        {
            public string name;
            public int size;
            public string material;
            public string normal;
            public string occlusion;
            public string emission;
        }

        [Serializable]
        private sealed class SkinInfo
        {
            public int id;
            public string key;
            public string name;
            public string description;
            public SkinSetInfo[] sets;
        }

        [Serializable]
        private sealed class SkinSetInfo
        {
            public string set;
            public string colour;
            public string metalSmooth;
            public string emission;
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
            public string textureSet;
        }

        private enum Map { Colour, Data, Normal }

        public static void EnsureAll()
        {
            foreach (var path in ManifestPaths()) Ensure(path);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Problems for the Validate list: an old-format manifest, a missing skin material, a model not remapped.</summary>
        public static IEnumerable<(string Message, Object Context)> Problems()
        {
            foreach (var path in ManifestPaths())
            {
                var manifest = Load(path, out var folder);
                if (manifest == null) { yield return ($"{path}: cannot read the kit manifest.", null); continue; }
                if (manifest.textureSets == null || manifest.textureSets.Length == 0 || manifest.skins == null || manifest.skins.Length == 0)
                {
                    yield return ($"Kit {manifest.kit}: old manifest (no texture sets or skins). Export the kit again from PartForge.", null);
                    continue;
                }
                if (AssetDatabase.LoadAssetAtPath<SkinLibrary>(LibraryPath(folder, manifest)) == null)
                    yield return ($"Kit {manifest.kit}: no skin library. Press '5. Import part kits'.", null);
                foreach (var set in manifest.textureSets)
                foreach (var skin in manifest.skins)
                    if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(folder, manifest, set.name, skin.key)) == null)
                        yield return ($"Kit {manifest.kit}: no material for set {set.name}, skin {skin.key}. Press '5. Import part kits'.", null);
                foreach (var part in manifest.parts ?? Array.Empty<PartInfo>())
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(folder, manifest, part.textureSet, manifest.skins[0].key));
                    if (AssetImporter.GetAtPath($"{folder}/{part.file}") is not ModelImporter importer)
                        yield return ($"Kit {manifest.kit}: model {part.file} is missing.", null);
                    else if (material == null || !importer.GetExternalObjectMap().ContainsValue(material))
                        yield return ($"Kit {manifest.kit}: {part.name} does not use its kit material. Press '5. Import part kits'.", importer);
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

        private static string MaterialPath(string folder, Manifest manifest, string set, string skin) =>
            $"{folder}/Materials/{manifest.kit}_{set}_{skin}.mat";

        private static string LibraryPath(string folder, Manifest manifest) => $"{folder}/{manifest.kit}_Skins.asset";

        private static void Ensure(string manifestPath)
        {
            var manifest = Load(manifestPath, out var folder);
            if (manifest?.textureSets == null || manifest.skins == null || manifest.skins.Length == 0)
            {
                Debug.LogWarning($"Setup: kit manifest {manifestPath} has no texture sets or skins (old format). Export the kit again from PartForge.");
                return;
            }

            // Materials: one per texture set and skin.
            var materials = new Dictionary<(string Set, string Skin), Material>();
            foreach (var set in manifest.textureSets)
            {
                var normal = Texture($"{folder}/{set.normal}", Map.Normal, set.size);
                var occlusion = Texture($"{folder}/{set.occlusion}", Map.Data, set.size);
                foreach (var skin in manifest.skins)
                {
                    var files = skin.sets?.FirstOrDefault(s => s.set == set.name);
                    if (files == null)
                    {
                        Debug.LogWarning($"Setup: kit {manifest.kit} skin {skin.key} has no maps for set {set.name}.");
                        continue;
                    }
                    var colour = Texture($"{folder}/{files.colour}", Map.Colour, set.size);
                    var metalSmooth = Texture($"{folder}/{files.metalSmooth}", Map.Data, set.size);
                    var emission = Texture($"{folder}/{files.emission}", Map.Colour, set.size);
                    var material = EnsureMaterial(MaterialPath(folder, manifest, set.name, skin.key), colour, metalSmooth, normal,
                        occlusion, emission);
                    if (material != null) materials[(set.name, skin.key)] = material;
                }
            }

            // Models: rigid props, material remapped to the default skin of their texture set.
            var defaultSkin = manifest.skins[0].key;
            foreach (var part in manifest.parts ?? Array.Empty<PartInfo>())
            {
                if (AssetImporter.GetAtPath($"{folder}/{part.file}") is not ModelImporter importer)
                {
                    Debug.LogWarning($"Setup: kit {manifest.kit} model {part.file} not found.");
                    continue;
                }
                var set = manifest.textureSets.FirstOrDefault(s => s.name == part.textureSet);
                if (set == null || !materials.TryGetValue((set.name, defaultSkin), out var material))
                {
                    Debug.LogWarning($"Setup: kit {manifest.kit} part {part.name} has no texture set material.");
                    continue;
                }

                var changed = false;
                // Rigid props: no animation, cameras, lights or colliders (colliders come from the chassis prefab).
                if (importer.importAnimation) { importer.importAnimation = false; changed = true; }
                if (importer.importCameras) { importer.importCameras = false; changed = true; }
                if (importer.importLights) { importer.importLights = false; changed = true; }
                if (importer.addCollider) { importer.addCollider = false; changed = true; }
                // Tangents for the normal map: Unity's MikkTSpace matches the Blender bake.
                if (importer.importTangents != ModelImporterTangents.CalculateMikk) { importer.importTangents = ModelImporterTangents.CalculateMikk; changed = true; }

                var source = new AssetImporter.SourceAssetIdentifier(typeof(Material), set.material);
                if (!importer.GetExternalObjectMap().TryGetValue(source, out var mapped) || mapped != material)
                {
                    importer.AddRemap(source, material);
                    changed = true;
                }
                if (changed) importer.SaveAndReimport();
            }

            var library = EnsureLibrary(folder, manifest, materials);
            if (manifest.kit == DefaultKit && library != null)
            {
                var config = ConfigAssets.Load();
                if (config != null && config.Skins == null)
                {
                    config.SetSkins(library);
                    EditorUtility.SetDirty(config);
                }
            }
        }

        private static SkinLibrary EnsureLibrary(string folder, Manifest manifest, Dictionary<(string Set, string Skin), Material> materials)
        {
            var path = LibraryPath(folder, manifest);
            var library = AssetDatabase.LoadAssetAtPath<SkinLibrary>(path);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<SkinLibrary>();
                AssetDatabase.CreateAsset(library, path);
            }
            var entries = manifest.skins.Select(skin => new SkinLibrary.Skin
            {
                Id = (ushort)skin.id,
                Key = skin.key,
                DisplayName = skin.name,
                Description = skin.description,
                Materials = manifest.textureSets
                    .Where(set => materials.ContainsKey((set.name, skin.key)))
                    .Select(set => new SkinLibrary.SetMaterial { Set = set.name, Material = materials[(set.name, skin.key)] })
                    .ToArray(),
            }).ToArray();
            library.Initialise(manifest.kit, entries);
            EditorUtility.SetDirty(library);
            return library;
        }

        /// <summary>Baked maps: trilinear with mipmaps, clamped (UV islands), sRGB only for colour; normal maps as normal maps.</summary>
        private static Texture2D Texture(string path, Map map, int size)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                var normal = map == Map.Normal;
                var srgb = map == Map.Colour;
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                var maxSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(size, 32)), 32, 8192);
                if (importer.textureType != type || importer.sRGBTexture != srgb || !importer.mipmapEnabled ||
                    importer.filterMode != FilterMode.Trilinear || importer.wrapMode != TextureWrapMode.Clamp ||
                    importer.maxTextureSize != maxSize)
                {
                    importer.textureType = type;
                    importer.sRGBTexture = srgb;
                    importer.mipmapEnabled = true;
                    importer.filterMode = FilterMode.Trilinear;
                    importer.anisoLevel = 4;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.maxTextureSize = maxSize;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.SaveAndReimport();
                }
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) Debug.LogWarning($"Setup: kit texture {path} not found.");
            return texture;
        }

        private static Material EnsureMaterial(string path, Texture2D colour, Texture2D metalSmooth, Texture2D normal,
            Texture2D occlusion, Texture2D emission)
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
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_OcclusionMap", occlusion);
            material.SetFloat("_OcclusionStrength", 1f);
            material.EnableKeyword("_OCCLUSIONMAP");
            // Lights and glow lines; bloom picks them up.
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
