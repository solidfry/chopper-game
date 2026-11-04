using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// City kits made by the CityForge generator (<c>Tools/CityForge</c>, Blender): each kit folder under
    /// <see cref="CityFolder"/> has a <c>*.manifest.json</c> (buildings, roads), a <c>*.terrain.json</c> (terrain chunks,
    /// backdrop, biome table, the layout) and the FBX models. This step sets the texture import settings, makes the
    /// building material (<see cref="BuildingShader"/>, URP Lit until the graph compiles) and the terrain material
    /// (<see cref="TerrainShader"/>), remaps the models to them, makes one prefab per building (box colliders and marker
    /// children from the manifest; the LOD Group comes from the FBX's _LOD0.._LOD2 names) and builds the city scene
    /// (<see cref="ScenePath"/>): terrain chunks with mesh colliders, the buildings at their layout spots, the backdrop
    /// and the terrain markers. Open that scene as a SubScene of the match scene, so Unity Physics bakes the colliders.
    /// Rebuilds the prefabs and the scene every time (they are generated: no hand edits to keep); materials are made once.
    /// Written without the editor (4 Nov 2026).
    /// </summary>
    internal static class CityAssets
    {
        public const string CityFolder = "Assets/Chopper/Art/City";
        public const string Cc0Folder = "Assets/Chopper/Art/ThirdParty/CC0";
        public const string PrefabFolder = "Assets/Chopper/Prefabs/City";
        public const string SceneFolder = "Assets/Scenes";
        /// <summary>The building shader graph (Art/Shaders/BuildingWear.shadergraph).</summary>
        public const string BuildingShader = "Shader Graphs/BuildingWear";
        /// <summary>The terrain shader graph (Art/Shaders/TerrainBiome.shadergraph).</summary>
        public const string TerrainShader = "Shader Graphs/TerrainBiome";
        /// <summary>The material name the terrain FBX files use (CityForge: "&lt;kit&gt;_Terrain").</summary>
        private const string TerrainMaterialSuffix = "_Terrain";
        private const float EmissionStrength = 4f;
        private const string MarkerPrefix = "MARKER_";

        public static string ScenePath(string kit) => $"{SceneFolder}/{kit}.unity";

        // -- manifests ----------------------------------------------------------------------------------------------------

        [Serializable]
        internal sealed class Manifest
        {
            public string kit;
            public PaletteInfo palette;
            public LodInfo lod;
            public BuildingInfo[] buildings;
            public TerrainRef terrain;
        }

        [Serializable]
        internal sealed class PaletteInfo
        {
            public string material;
            public string colour;
            public string emission;
            public string metalSmooth;
        }

        [Serializable]
        internal sealed class LodInfo
        {
            public int levels;
            public float[] screenHeights;
        }

        [Serializable]
        internal sealed class BuildingInfo
        {
            public string name;
            public string type;
            public string file;
            public ColliderInfo[] colliders;
            public MarkerInfo[] markers;
        }

        [Serializable]
        internal sealed class ColliderInfo
        {
            public float[] centre;
            public float[] size;
            /// <summary>Unity euler Y, degrees.</summary>
            public float yaw;
        }

        [Serializable]
        internal sealed class MarkerInfo
        {
            public string name;
            public string kind;
            public int index;
            public float[] position;
            public float radius;
        }

        [Serializable]
        internal sealed class TerrainRef
        {
            public string file;
        }

        [Serializable]
        internal sealed class TerrainData
        {
            public string kit;
            public BiomeInfo biomes;
            public ChunkInfo[] chunks;
            public BackdropInfo backdrop;
            public AtmosphereInfo atmosphere;
            public TerrainMarker[] markers;
            public Placement[] layout;
        }

        [Serializable]
        internal sealed class BiomeInfo
        {
            public string file;
            public string[] biomes;
            public string current;
            public string masks;
        }

        [Serializable]
        internal sealed class ChunkInfo
        {
            public string name;
            public string file;
        }

        [Serializable]
        internal sealed class BackdropInfo
        {
            public ChunkInfo[] parts;
        }

        [Serializable]
        internal sealed class AtmosphereInfo
        {
            public float fogStart;
            public float fogScale;
        }

        [Serializable]
        internal sealed class TerrainMarker
        {
            public string kind;
            public int index;
            public float[] position;
            public float radius;
        }

        [Serializable]
        internal sealed class Placement
        {
            public string building;
            public float[] position;
            public float yaw;
        }

        // -- entry points -------------------------------------------------------------------------------------------------

        public static void EnsureAll()
        {
            foreach (var path in ManifestPaths()) Ensure(path);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Problems for the Validate list: missing materials, prefabs or the city scene.</summary>
        public static IEnumerable<(string Message, Object Context)> Problems()
        {
            foreach (var path in ManifestPaths())
            {
                var manifest = Load<Manifest>(path, out var folder);
                if (manifest?.buildings == null) { yield return ($"{path}: cannot read the city manifest.", null); continue; }
                if (AssetDatabase.LoadAssetAtPath<Material>(BuildingMaterialPath(folder, manifest.kit)) == null)
                    yield return ($"City {manifest.kit}: no building material. Press '8. Import city (CityForge)'.", null);
                else if (Shader.Find(BuildingShader) == null)
                    yield return ($"City {manifest.kit}: shader '{BuildingShader}' missing or not compiled; buildings use URP Lit " +
                                  "(no wear). See Assets/Chopper/Art/Shaders/README.md.", null);
                var missing = manifest.buildings.Count(b => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(manifest.kit, b.name)) == null);
                if (missing > 0)
                    yield return ($"City {manifest.kit}: {missing} building prefab(s) missing. Press '8. Import city (CityForge)'.", null);
                if (manifest.terrain != null && !string.IsNullOrEmpty(manifest.terrain.file))
                {
                    if (AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath(folder, manifest.kit)) == null)
                        yield return ($"City {manifest.kit}: no terrain material. Press '8. Import city (CityForge)'.", null);
                    if (!File.Exists(ScenePath(manifest.kit)))
                        yield return ($"City {manifest.kit}: no city scene ({ScenePath(manifest.kit)}). Press '8. Import city (CityForge)'.", null);
                }
            }
        }

        private static IEnumerable<string> ManifestPaths() => Directory.Exists(CityFolder)
            ? Directory.GetFiles(CityFolder, "*.manifest.json", SearchOption.AllDirectories)
            : Array.Empty<string>();

        private static T Load<T>(string path, out string folder) where T : class
        {
            folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            return File.Exists(path) ? JsonUtility.FromJson<T>(File.ReadAllText(path)) : null;
        }

        private static string BuildingMaterialPath(string folder, string kit) => $"{folder}/Materials/{kit}_Buildings.mat";
        private static string TerrainMaterialPath(string folder, string kit) => $"{folder}/Materials/{kit}_Terrain.mat";
        private static string PrefabPath(string kit, string building) => $"{PrefabFolder}/{kit}/{building}.prefab";

        private static void Ensure(string manifestPath)
        {
            var manifest = Load<Manifest>(manifestPath, out var folder);
            if (manifest?.buildings == null || manifest.palette == null)
            {
                Debug.LogWarning($"Setup: city manifest {manifestPath} cannot be read. Export the city again from CityForge.");
                return;
            }
            var terrain = manifest.terrain != null && !string.IsNullOrEmpty(manifest.terrain.file)
                ? Load<TerrainData>($"{folder}/{manifest.terrain.file}", out _)
                : null;

            var table = terrain?.biomes != null ? DataTexture($"{folder}/{terrain.biomes.file}", point: true) : null;
            var biomeIndex = terrain?.biomes?.biomes != null ? Mathf.Max(0, Array.IndexOf(terrain.biomes.biomes, terrain.biomes.current)) : 0;

            var buildingMaterial = EnsureBuildingMaterial(folder, manifest, table, biomeIndex);
            var terrainMaterial = terrain != null ? EnsureTerrainMaterial(folder, manifest.kit, table, biomeIndex) : null;

            foreach (var building in manifest.buildings)
                Remap($"{folder}/{building.file}", buildingMaterial, manifest.palette.material, null, null);
            if (terrain != null)
            {
                var terrainSource = manifest.kit + TerrainMaterialSuffix;
                foreach (var chunk in terrain.chunks ?? Array.Empty<ChunkInfo>())
                    Remap($"{folder}/{chunk.file}", terrainMaterial, terrainSource, null, null, readable: true);
                foreach (var part in terrain.backdrop?.parts ?? Array.Empty<ChunkInfo>())
                    Remap($"{folder}/{part.file}", terrainMaterial, terrainSource, buildingMaterial, manifest.palette.material);
            }

            var prefabs = new Dictionary<string, GameObject>();
            foreach (var building in manifest.buildings)
            {
                var prefab = EnsurePrefab(folder, manifest, building);
                if (prefab != null) prefabs[building.name] = prefab;
            }
            if (terrain != null) BuildScene(folder, manifest, terrain, prefabs);
        }

        // -- textures and materials ---------------------------------------------------------------------------------------

        /// <summary>Data maps (palette cells, biome table): point filtered, no mipmaps, uncompressed; sRGB only for colours.</summary>
        private static Texture2D DataTexture(string path, bool point, bool srgb = false)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                var filter = point ? FilterMode.Point : FilterMode.Bilinear;
                if (importer.filterMode != filter || importer.mipmapEnabled || importer.sRGBTexture != srgb ||
                    importer.textureCompression != TextureImporterCompression.Uncompressed || importer.wrapMode != TextureWrapMode.Clamp ||
                    importer.npotScale != TextureImporterNPOTScale.None)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.filterMode = filter;
                    importer.mipmapEnabled = false;
                    importer.sRGBTexture = srgb;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) Debug.LogWarning($"Setup: city texture {path} not found.");
            return texture;
        }

        /// <summary>CC0 detail maps (Tools/CityForge/fetch_cc0.py): repeat, mipmaps; normal maps as normal maps. Null if not fetched.</summary>
        private static Texture2D DetailTexture(string name, bool normal)
        {
            var path = $"{Cc0Folder}/{name}/{name}_{(normal ? "Normal" : "Colour")}.jpg";
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                Debug.LogWarning($"Setup: CC0 detail map {path} not found (run Tools/CityForge/fetch_cc0.py).");
                return null;
            }
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type || importer.wrapMode != TextureWrapMode.Repeat || !importer.mipmapEnabled ||
                importer.sRGBTexture == normal)
            {
                importer.textureType = type;
                importer.sRGBTexture = !normal;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material EnsureMaterialAsset(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                // URP Lit stand-in from a run before the graph compiled: switch to the graph now.
                material.shader = shader;
            }
            return material;
        }

        /// <summary>Buildings, roads and the skyline: the BuildingWear graph on the palette maps (URP Lit on the same maps
        /// until the graph exists).</summary>
        private static Material EnsureBuildingMaterial(string folder, Manifest manifest, Texture2D table, int biomeIndex)
        {
            var info = manifest.palette;
            var colour = DataTexture($"{folder}/{info.colour}", point: true, srgb: true);
            var emission = DataTexture($"{folder}/{info.emission}", point: true, srgb: true);
            var metalSmooth = DataTexture($"{folder}/{info.metalSmooth}", point: true);
            var graph = Shader.Find(BuildingShader);
            var shader = graph != null ? graph : Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null || colour == null) return null;
            if (graph == null)
                Debug.LogWarning($"Setup: no '{BuildingShader}' shader yet; city buildings use URP Lit (no wear). " +
                                 "See Assets/Chopper/Art/Shaders/README.md, then press '8. Import city' again.");

            var material = EnsureMaterialAsset(BuildingMaterialPath(folder, manifest.kit), shader);
            if (graph != null)
            {
                material.SetTexture("_PaletteColour", colour);
                material.SetTexture("_PaletteEmission", emission);
                material.SetTexture("_PaletteMetalSmooth", metalSmooth);
                material.SetTexture("_BiomeTable", table);
                material.SetFloat("_BiomeIndex", biomeIndex);
                material.SetTexture("_ConcreteColour", DetailTexture("ConcreteWall", false));
                material.SetTexture("_ConcreteNormal", DetailTexture("ConcreteWall", true));
                if (!material.HasFloat("_Wear") || material.GetFloat("_Wear") <= 0f) material.SetFloat("_Wear", 1f);
                material.SetFloat("_EmissionStrength", EmissionStrength);
            }
            else
            {
                material.SetTexture("_BaseMap", colour);
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_MetallicGlossMap", metalSmooth);
                material.SetFloat("_Smoothness", 1f);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.SetTexture("_EmissionMap", emission);
                material.SetColor("_EmissionColor", Color.white * EmissionStrength);
                material.EnableKeyword("_EMISSION");
            }
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Terrain chunks and the backdrop: the TerrainBiome graph (URP Lit in sand colour until it exists).</summary>
        private static Material EnsureTerrainMaterial(string folder, string kit, Texture2D table, int biomeIndex)
        {
            var graph = Shader.Find(TerrainShader);
            var shader = graph != null ? graph : Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            if (graph == null)
                Debug.LogWarning($"Setup: no '{TerrainShader}' shader yet; the city terrain is plain URP Lit. " +
                                 "See Assets/Chopper/Art/Shaders/README.md, then press '8. Import city' again.");

            var material = EnsureMaterialAsset(TerrainMaterialPath(folder, kit), shader);
            if (graph != null)
            {
                material.SetTexture("_BiomeTable", table);
                material.SetFloat("_BiomeIndex", biomeIndex);
                material.SetTexture("_RockColour", DetailTexture("RockFace", false));
                material.SetTexture("_RockNormal", DetailTexture("RockFace", true));
                material.SetTexture("_SandColour", DetailTexture("GravellySand", false));
                material.SetTexture("_SandNormal", DetailTexture("GravellySand", true));
                material.SetTexture("_MudColour", DetailTexture("DryMud", false));
                material.SetTexture("_CoverColour", DetailTexture("WindSnow", false));
                if (!material.HasFloat("_DetailStrength") || material.GetFloat("_DetailStrength") <= 0f) material.SetFloat("_DetailStrength", 1f);
            }
            else
            {
                material.SetColor("_BaseColor", new Color(0.55f, 0.47f, 0.37f));
                material.SetFloat("_Smoothness", 0.1f);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        // -- models -------------------------------------------------------------------------------------------------------

        /// <summary>Static props: no animation, cameras, lights or importer colliders (colliders come from the manifest);
        /// FBX materials remapped to ours. Vertex colours (the wear and biome masks) are kept by default.</summary>
        private static void Remap(string path, Material material, string source, Material second, string secondSource, bool readable = false)
        {
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
            {
                Debug.LogWarning($"Setup: city model {path} not found.");
                return;
            }
            var changed = false;
            if (importer.importAnimation) { importer.importAnimation = false; changed = true; }
            if (importer.importCameras) { importer.importCameras = false; changed = true; }
            if (importer.importLights) { importer.importLights = false; changed = true; }
            if (importer.addCollider) { importer.addCollider = false; changed = true; }
            if (importer.importBlendShapes) { importer.importBlendShapes = false; changed = true; }
            if (importer.isReadable != readable) { importer.isReadable = readable; changed = true; }
            // Flat-shaded low poly: keep the FBX normals (hard edges), no tangents needed (world-space normal output).
            if (importer.importNormals != ModelImporterNormals.Import) { importer.importNormals = ModelImporterNormals.Import; changed = true; }
            if (importer.importTangents != ModelImporterTangents.None) { importer.importTangents = ModelImporterTangents.None; changed = true; }
            changed |= AddRemap(importer, source, material);
            if (second != null && !string.IsNullOrEmpty(secondSource)) changed |= AddRemap(importer, secondSource, second);
            if (changed) importer.SaveAndReimport();
        }

        private static bool AddRemap(ModelImporter importer, string source, Material material)
        {
            if (material == null || string.IsNullOrEmpty(source)) return false;
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), source);
            if (importer.GetExternalObjectMap().TryGetValue(id, out var mapped) && mapped == material) return false;
            importer.AddRemap(id, material);
            return true;
        }

        // -- prefabs ------------------------------------------------------------------------------------------------------

        /// <summary>One prefab per building: the model (with its LOD Group), a "Colliders" child with one box per manifest
        /// collider (static: Unity Physics bakes them in the subscene) and a "Markers" child (RoofPad, Roof).</summary>
        private static GameObject EnsurePrefab(string folder, Manifest manifest, BuildingInfo building)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{building.file}");
            if (model == null) return null;
            var path = PrefabPath(manifest.kit, building.name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var root = new GameObject(building.name) { isStatic = true };
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = "Model";
                instance.transform.SetParent(root.transform, false);
                SetLodHeights(instance, manifest.lod);
                foreach (var child in instance.GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.isStatic = true;
                    // Marker empties in the FBX: the "Markers" child holds them with radius and kind.
                    if (child != instance.transform && child.name.StartsWith(MarkerPrefix, StringComparison.Ordinal))
                        child.gameObject.SetActive(false);
                }

                var colliders = new GameObject("Colliders") { isStatic = true };
                colliders.transform.SetParent(root.transform, false);
                var index = 0;
                foreach (var info in building.colliders ?? Array.Empty<ColliderInfo>())
                {
                    var box = new GameObject($"Box_{index++}") { isStatic = true };
                    box.transform.SetParent(colliders.transform, false);
                    box.transform.localPosition = Vector(info.centre);
                    box.transform.localRotation = Quaternion.Euler(0f, info.yaw, 0f);
                    box.AddComponent<BoxCollider>().size = Vector(info.size);
                }

                if (building.markers is { Length: > 0 })
                {
                    var markers = new GameObject("Markers") { isStatic = true };
                    markers.transform.SetParent(root.transform, false);
                    foreach (var info in building.markers)
                    {
                        var marker = new GameObject($"{info.kind}_{info.index}_r{info.radius:0.#}") { isStatic = true };
                        marker.transform.SetParent(markers.transform, false);
                        marker.transform.localPosition = Vector(info.position);
                    }
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>The manifest's LOD screen heights on the model's LOD Group (made by the importer from the names).</summary>
        private static void SetLodHeights(GameObject model, LodInfo info)
        {
            var group = model.GetComponentInChildren<LODGroup>(true);
            if (group == null || info?.screenHeights == null) return;
            var lods = group.GetLODs();
            for (var i = 0; i < lods.Length && i < info.screenHeights.Length; i++)
                lods[i].screenRelativeTransitionHeight = info.screenHeights[i];
            group.SetLODs(lods);
        }

        private static Vector3 Vector(float[] v) => v is { Length: >= 3 } ? new Vector3(v[0], v[1], v[2]) : Vector3.zero;

        // -- scene --------------------------------------------------------------------------------------------------------

        /// <summary>
        /// The city scene (made anew every time): "Terrain" (chunks, mesh collider from each chunk's LOD0), "Backdrop" (no
        /// colliders), "Buildings" (prefab instances at the layout spots) and "Markers" (MesaTop). Meant to be a SubScene.
        /// </summary>
        private static void BuildScene(string folder, Manifest manifest, TerrainData terrain, Dictionary<string, GameObject> prefabs)
        {
            var path = ScenePath(manifest.kit);
            if (SceneManager.GetSceneByPath(path).isLoaded)
            {
                Debug.LogWarning($"Setup: city scene {path} is open (SubScene in edit mode?). Close it, then press '8. Import city' again.");
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var terrainRoot = new GameObject("Terrain") { isStatic = true };
                SceneManager.MoveGameObjectToScene(terrainRoot, scene);
                foreach (var chunk in terrain.chunks ?? Array.Empty<ChunkInfo>())
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{chunk.file}");
                    if (model == null) continue;
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                    instance.transform.SetParent(terrainRoot.transform, false);
                    SetLodHeights(instance, manifest.lod);
                    SetStatic(instance);
                    var lod0 = instance.GetComponentsInChildren<MeshFilter>(true)
                        .FirstOrDefault(f => f.sharedMesh != null && f.name.EndsWith("_LOD0", StringComparison.Ordinal));
                    if (lod0 != null) instance.AddComponent<MeshCollider>().sharedMesh = lod0.sharedMesh;
                }

                var backdropRoot = new GameObject("Backdrop") { isStatic = true };
                SceneManager.MoveGameObjectToScene(backdropRoot, scene);
                foreach (var part in terrain.backdrop?.parts ?? Array.Empty<ChunkInfo>())
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{part.file}");
                    if (model == null) continue;
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                    instance.transform.SetParent(backdropRoot.transform, false);
                    SetStatic(instance);
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                var buildingRoot = new GameObject("Buildings") { isStatic = true };
                SceneManager.MoveGameObjectToScene(buildingRoot, scene);
                var missing = new HashSet<string>();
                foreach (var placement in terrain.layout ?? Array.Empty<Placement>())
                {
                    if (!prefabs.TryGetValue(placement.building, out var prefab)) { missing.Add(placement.building); continue; }
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    instance.transform.SetParent(buildingRoot.transform, false);
                    instance.transform.SetLocalPositionAndRotation(Vector(placement.position), Quaternion.Euler(0f, placement.yaw, 0f));
                }
                if (missing.Count > 0)
                    Debug.LogWarning($"Setup: city {manifest.kit} layout names buildings with no prefab: {string.Join(", ", missing)}.");

                var markerRoot = new GameObject("Markers") { isStatic = true };
                SceneManager.MoveGameObjectToScene(markerRoot, scene);
                foreach (var info in terrain.markers ?? Array.Empty<TerrainMarker>())
                {
                    var marker = new GameObject($"{info.kind}_{info.index}_r{info.radius:0.#}") { isStatic = true };
                    marker.transform.SetParent(markerRoot.transform, false);
                    marker.transform.localPosition = Vector(info.position);
                }

                Directory.CreateDirectory(SceneFolder);
                EditorSceneManager.SaveScene(scene, path);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            if (terrain.atmosphere != null && terrain.atmosphere.fogScale > 0f)
                Debug.Log($"Setup: city {manifest.kit} scene saved to {path}. Suggested fog: exponential, density " +
                          $"{1f / terrain.atmosphere.fogScale:0.#####}; camera far clip 20000 m for the backdrop. " +
                          "Add the scene as a SubScene of the match scene.");
        }

        private static void SetStatic(GameObject root)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.isStatic = true;
        }
    }
}
