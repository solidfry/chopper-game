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
    /// material per texture set and skin (baked skins), one skin-shader material per texture set (runtime skins, when the
    /// <see cref="RuntimeShader"/> graph exists), remaps every model to its set's default-skin material, fills the kit's
    /// <see cref="SkinLibrary"/> and rebuilds its skin table (the default kit's library goes into the game config).
    /// Creates only what is missing (materials are left alone once made). Linking kit models to chassis and modules is
    /// <see cref="KitChassis"/> (step 6).
    /// </summary>
    internal static class KitAssets
    {
        public const string KitsFolder = "Assets/Chopper/Art/Kits";
        /// <summary>The kit whose skins the game uses (PartForge's default style, "dystopia").</summary>
        public const string DefaultKit = "Kit02";
        /// <summary>The runtime skin shader graph (Art/Shaders/ChopperSkin.shadergraph, see Art/Shaders/README.md).</summary>
        public const string RuntimeShader = "Shader Graphs/ChopperSkin";

        [Serializable]
        internal sealed class Manifest
        {
            public string kit;
            public TextureSetInfo[] textureSets;
            public SkinInfo[] skins;
            public PartInfo[] parts;
        }

        [Serializable]
        internal sealed class TextureSetInfo
        {
            public string name;
            public int size;
            public string material;
            public string normal;
            public string occlusion;
            public string emission;
            public string skinMask;
            public string skinWear;
            public float[] heightRange;
            public string baseSkin;
        }

        [Serializable]
        internal sealed class SkinInfo
        {
            public int id;
            public string key;
            public string name;
            public string description;
            public SkinSetInfo[] sets;
            public SkinParams runtime;
        }

        [Serializable]
        internal sealed class SkinSetInfo
        {
            public string set;
            public string colour;
            public string metalSmooth;
            public string emission;
        }

        [Serializable]
        internal sealed class PartInfo
        {
            public string name;
            public string file;
            public string kind;
            public string slot;
            public int[] moduleIds;
            public int triangles;
            public string textureSet;
            /// <summary>Chassis only: the socket marker nodes in the model.</summary>
            public SocketInfo[] sockets;
        }

        [Serializable]
        internal sealed class SocketInfo
        {
            /// <summary>Marker node name in the model (SOCKET_Pylon_1).</summary>
            public string name;
            public string slot;
            public int index;
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
                var library = AssetDatabase.LoadAssetAtPath<SkinLibrary>(LibraryPath(folder, manifest));
                if (library == null)
                    yield return ($"Kit {manifest.kit}: no skin library. Press '5. Import part kits'.", null);
                foreach (var set in manifest.textureSets)
                foreach (var skin in manifest.skins)
                    if (skin.sets != null && skin.sets.Any(s => s.set == set.name) &&
                        AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(folder, manifest, set.name, skin.key)) == null)
                        yield return ($"Kit {manifest.kit}: no material for set {set.name}, skin {skin.key}. Press '5. Import part kits'.", null);
                foreach (var part in manifest.parts ?? Array.Empty<PartInfo>())
                {
                    var material = ModelMaterial(folder, manifest, part.textureSet, library);
                    if (AssetImporter.GetAtPath($"{folder}/{part.file}") is not ModelImporter importer)
                        yield return ($"Kit {manifest.kit}: model {part.file} is missing.", null);
                    else if (material == null || !importer.GetExternalObjectMap().ContainsValue(material))
                        yield return ($"Kit {manifest.kit}: {part.name} does not use its kit material. Press '5. Import part kits'.", importer);
                }
            }
        }

        /// <summary>The manifest of one kit (<see cref="DefaultKit"/> for the game's chassis), or null.</summary>
        public static Manifest LoadKit(string kit, out string folder)
        {
            folder = null;
            var path = ManifestPaths().FirstOrDefault(p => Path.GetFileName(p) == $"{kit}.manifest.json");
            return path != null ? Load(path, out folder) : null;
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
                        // Runtime-only skin (no baked maps): a baked material from an older export is stale.
                        var stale = MaterialPath(folder, manifest, set.name, skin.key);
                        if (AssetDatabase.LoadAssetAtPath<Material>(stale) != null) AssetDatabase.DeleteAsset(stale);
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

            var library = EnsureLibrary(folder, manifest, materials);
            if (library != null) EnsureRuntime(folder, manifest, library);
            if (manifest.kit == DefaultKit && library != null)
            {
                var config = ConfigAssets.Load();
                if (config != null && config.Skins == null)
                {
                    config.SetSkins(library);
                    EditorUtility.SetDirty(config);
                }
            }

            // Models: rigid props, material remapped to their texture set's runtime skin material (the default skin's
            // baked material until the shader graph exists).
            foreach (var part in manifest.parts ?? Array.Empty<PartInfo>())
            {
                if (AssetImporter.GetAtPath($"{folder}/{part.file}") is not ModelImporter importer)
                {
                    Debug.LogWarning($"Setup: kit {manifest.kit} model {part.file} not found.");
                    continue;
                }
                var set = manifest.textureSets.FirstOrDefault(s => s.name == part.textureSet);
                var material = set != null ? ModelMaterial(folder, manifest, set.name, library) : null;
                if (material == null)
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
        }

        /// <summary>The material a kit model uses: its set's runtime skin material, else the default skin's baked one.</summary>
        private static Material ModelMaterial(string folder, Manifest manifest, string set, SkinLibrary library)
        {
            var runtime = library != null ? library.RuntimeMaterial(set) : null;
            return runtime != null ? runtime : AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(folder, manifest, set, manifest.skins[0].key));
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
                Look = skin.runtime != null ? skin.runtime.ToLook() : new SkinLook(),
                Materials = manifest.textureSets
                    .Where(set => materials.ContainsKey((set.name, skin.key)))
                    .Select(set => new SkinLibrary.SetMaterial { Set = set.name, Material = materials[(set.name, skin.key)] })
                    .ToArray(),
            }).ToArray();
            library.Initialise(manifest.kit, entries);
            EditorUtility.SetDirty(library);
            return library;
        }

        /// <summary>Runtime skins: the skin table (a sub-asset of the library, rebuilt from the looks every time) and one
        /// skin-shader material per texture set (made once; needs the shader graph).</summary>
        private static void EnsureRuntime(string folder, Manifest manifest, SkinLibrary library)
        {
            var looks = library.Looks();
            var height = Mathf.Max(looks.Length, 1);
            var table = library.Table;
            if (table == null)
            {
                table = new Texture2D(SkinTable.Width, height, TextureFormat.RGBAHalf, false, true) { name = $"{manifest.kit}_SkinTable" };
                AssetDatabase.AddObjectToAsset(table, library);
            }
            else if (table.height != height || table.width != SkinTable.Width)
            {
                table.Reinitialize(SkinTable.Width, height, TextureFormat.RGBAHalf, false);
            }
            table.filterMode = FilterMode.Point;
            table.wrapMode = TextureWrapMode.Clamp;
            table.SetPixels(SkinTable.Pixels(looks));
            table.Apply(false, false);
            EditorUtility.SetDirty(table);

            var shader = Shader.Find(RuntimeShader);
            if (shader == null)
            {
                Debug.LogWarning($"Setup: no '{RuntimeShader}' shader yet, so no runtime skin materials for kit {manifest.kit}. " +
                                 "Make the shader graph (Assets/Chopper/Art/Shaders/README.md), then press '5. Import part kits' again.");
                library.SetRuntime(library.RuntimeMaterials.ToArray(), table);
                return;
            }

            var runtimeMaterials = new List<SkinLibrary.SetMaterial>();
            foreach (var set in manifest.textureSets)
            {
                var path = $"{folder}/Materials/{manifest.kit}_{set.name}_Runtime.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    var baseSkin = manifest.skins.FirstOrDefault(s => s.key == set.baseSkin) ?? manifest.skins[0];
                    var files = baseSkin.sets?.FirstOrDefault(s => s.set == set.name);
                    if (files == null || string.IsNullOrEmpty(set.skinMask)) continue;
                    material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                    material.SetTexture("_BaseColourMap", Texture($"{folder}/{files.colour}", Map.Colour, set.size));
                    material.SetTexture("_BaseMetalSmooth", Texture($"{folder}/{files.metalSmooth}", Map.Data, set.size));
                    material.SetTexture("_BaseEmission", Texture($"{folder}/{set.emission}", Map.Colour, set.size));
                    material.SetTexture("_NormalMap", Texture($"{folder}/{set.normal}", Map.Normal, set.size));
                    material.SetTexture("_OcclusionMap", Texture($"{folder}/{set.occlusion}", Map.Data, set.size));
                    material.SetTexture("_SkinMask", Texture($"{folder}/{set.skinMask}", Map.Data, set.size));
                    material.SetTexture("_SkinWear", Texture($"{folder}/{set.skinWear}", Map.Data, set.size));
                    material.SetFloat("_EmissionStrength", 3f);
                    if (set.heightRange is { Length: >= 2 })
                        material.SetVector("_HeightRange", new Vector4(set.heightRange[0], set.heightRange[1], 0f, 0f));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    AssetDatabase.CreateAsset(material, path);
                }
                // The table is rebuilt in place, but a new library has a new table: always point at the current one.
                material.SetTexture("_SkinTable", table);
                material.SetFloat(SkinLibrary.SkinIndexProperty, 0f);
                EditorUtility.SetDirty(material);
                runtimeMaterials.Add(new SkinLibrary.SetMaterial { Set = set.name, Material = material });
            }
            library.SetRuntime(runtimeMaterials.ToArray(), table);
            EditorUtility.SetDirty(library);
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
