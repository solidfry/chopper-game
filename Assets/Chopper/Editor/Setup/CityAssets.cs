using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chopper.Simulation.Visuals;
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
            public float size;
            public BiomeInfo biomes;
            public SurfaceInfo surface;
            public ChunkInfo[] chunks;
            public BackdropInfo backdrop;
            public AtmosphereInfo atmosphere;
            public TerrainMarker[] markers;
            public Placement[] layout;
            public CityLight[] lights;
            public CityEmitter[] emitters;
            public HeightFogInfo heightFog;
            public TrafficInfo traffic;
            public LavaInfo[] lava;
            public FlightBoundsInfo flightBounds;
        }

        /// <summary>A lava centre line (flat x, y, z points every 8 m on the surface) and its width (m).</summary>
        [Serializable]
        internal sealed class LavaInfo
        {
            public float[] points;
            public float width;
        }

        /// <summary>Cosmetic traffic paths (CityForge traffic.py): ground lanes and air routes.</summary>
        [Serializable]
        internal sealed class TrafficInfo
        {
            public TrafficPathInfo[] lanes;
            public TrafficPathInfo[] air;
            public SearchlightInfo[] searchlights;
            public CraneInfo cranes;
        }

        /// <summary>Tower cranes: floodlight spots under the jib (local z, m) and each crane's pivot and slew.</summary>
        [Serializable]
        internal sealed class CraneInfo
        {
            public float[] floods;
            public CraneItem[] items;
        }

        [Serializable]
        internal sealed class CraneItem
        {
            public float[] pivot;
            public float phase;
            public float speed;
            public float swing;
        }

        [Serializable]
        internal sealed class SearchlightInfo
        {
            public float[] position;
            public float phase;
            public float speed;
            public string colour;
        }

        [Serializable]
        internal sealed class TrafficPathInfo
        {
            public string kind;
            public bool closed;
            public float speed;
            /// <summary>Flat x, y, z list (JsonUtility cannot read nested arrays).</summary>
            public float[] points;
            /// <summary>Flat list: distance along the lane (m), signal phase.</summary>
            public float[] stops;
        }

        /// <summary>The box players may fly in (centre and size, m).</summary>
        [Serializable]
        internal sealed class FlightBoundsInfo
        {
            public float[] centre;
            public float[] size;
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

        /// <summary>Map-wide surface textures for the terrain shader (CityForge terrain.Terrain.surface).</summary>
        [Serializable]
        internal sealed class SurfaceInfo
        {
            public string masks;
            public string normal;
            public float size;
            public int resolution;
        }

        /// <summary>A real light from CityForge (city space): street, flood, pad, flame, lava, shanty.</summary>
        [Serializable]
        internal sealed class CityLight
        {
            public string kind;
            public float[] position;
            public string colour;
            public float range;
            public float intensity;
        }

        /// <summary>A steam, smoke or heat outlet from CityForge (city space; position = top centre of the outlet).</summary>
        [Serializable]
        internal sealed class CityEmitter
        {
            public string kind;
            public float[] position;
            public float radius;
        }

        /// <summary>The map's height fog (CityForge build.HEIGHT_FOG per biome).</summary>
        [Serializable]
        internal sealed class HeightFogInfo
        {
            public string colour;
            public float density;
            public float @base;
            public float falloff;
            public float most;
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

        /// <summary>Every exported city: its kit name, folder and terrain data (null when it has none).</summary>
        internal static IEnumerable<(string Kit, string Folder, TerrainData Terrain)> Maps()
        {
            foreach (var path in ManifestPaths())
            {
                var manifest = Load<Manifest>(path, out var folder);
                if (manifest == null || string.IsNullOrEmpty(manifest.kit)) continue;
                var terrain = manifest.terrain != null && !string.IsNullOrEmpty(manifest.terrain.file)
                    ? Load<TerrainData>($"{folder}/{manifest.terrain.file}", out _)
                    : null;
                yield return (manifest.kit, folder, terrain);
            }
        }

        internal static Vector3 ToVector(float[] value) => Vector(value);

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

            var pools = terrain != null ? EnsureLightPools(folder, manifest.kit, terrain) : null;
            var buildingMaterial = EnsureBuildingMaterial(folder, manifest, table, biomeIndex);
            var terrainMaterial = terrain != null ? EnsureTerrainMaterial(folder, manifest.kit, table, biomeIndex, terrain.surface) : null;
            foreach (var material in new[] { buildingMaterial, terrainMaterial })
            {
                if (material == null || !material.HasTexture("_LightPools")) continue;
                material.SetTexture("_LightPools", pools);
                material.SetFloat("_MapSize", pools != null ? terrain.size : 0f);
                EditorUtility.SetDirty(material);
            }

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
            if (terrain != null) BuildScene(folder, manifest, terrain, prefabs, buildingMaterial);
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

        /// <summary>Map-wide surface maps (masks, world normals): linear, bilinear, mipmaps, clamp, high-quality
        /// compression, full size.</summary>
        private static Texture2D SurfaceTexture(string path, int resolution)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                Debug.LogWarning($"Setup: terrain surface map {path} not found. Export the city again from CityForge.");
                return null;
            }
            var size = Mathf.Max(2048, Mathf.NextPowerOfTwo(Mathf.Max(1, resolution)));
            if (importer.sRGBTexture || !importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp ||
                importer.filterMode != FilterMode.Bilinear || importer.maxTextureSize < size ||
                importer.textureCompression != TextureImporterCompression.CompressedHQ)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = size;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // -- light pools ----------------------------------------------------------------------------------------------

        /// <summary>Lamp kinds that light the ground below them, and the pool radius as a share of the light range.</summary>
        private static readonly string[] PoolKinds = { "street", "flood", "shanty", "pad" };
        private const float PoolRadius = 0.75f;
        /// <summary>CityForge intensity x this = pool light on the ground (albedo x pool = emission). Tuned by eye.</summary>
        private const float PoolBrightness = 0.9f;
        private const int PoolResolution = 1024;
        /// <summary>Brightest pool (linear), so many lamps close together stay a pool, not a white patch.</summary>
        private const float MaxPool = 1.2f;

        /// <summary>
        /// <c>&lt;kit&gt;_LightPools.exr</c>: every lamp's pool on the ground, map-wide (Art/Shaders/CityLightPools.hlsl).
        /// rgb = summed light (linear), a = height of the strongest lamp. Half float, no compression (alpha is height).
        /// </summary>
        private static Texture2D EnsureLightPools(string folder, string kit, TerrainData terrain)
        {
            if (terrain.lights == null || terrain.size <= 0f) return null;
            var n = PoolResolution;
            var texel = terrain.size / n;
            var light = new Color[n * n];
            var strongest = new float[n * n];
            var used = 0;
            foreach (var info in terrain.lights)
            {
                if (info?.position is not { Length: 3 } || Array.IndexOf(PoolKinds, info.kind) < 0) continue;
                var p = Vector(info.position);
                var colour = (ColorUtility.TryParseHtmlString(info.colour, out var c) ? c : new Color(1f, 0.7f, 0.35f)).linear;
                var power = info.intensity * PoolBrightness;
                var radius = Mathf.Max(3f, info.range * PoolRadius);
                int cx = Mathf.FloorToInt((p.x / terrain.size + 0.5f) * n), cz = Mathf.FloorToInt((0.5f - p.z / terrain.size) * n);
                var reach = Mathf.CeilToInt(radius / texel) + 1;
                for (var z = cz - reach; z <= cz + reach; z++)
                for (var x = cx - reach; x <= cx + reach; x++)
                {
                    if (x < 0 || z < 0 || x >= n || z >= n) continue;
                    var wx = ((x + 0.5f) / n - 0.5f) * terrain.size;
                    var wz = (0.5f - (z + 0.5f) / n) * terrain.size;
                    var d = new Vector2(wx - p.x, wz - p.z).magnitude / radius;
                    if (d >= 1f) continue;
                    var fall = (1f - d * d) * (1f - d * d);
                    var i = z * n + x;
                    light[i] += colour * (power * fall);
                    if (power * fall > strongest[i])
                    {
                        strongest[i] = power * fall;
                        light[i].a = p.y;
                    }
                }
                used++;
            }
            // Close lamps add up (helipad rims): cap the pool so roofs and pads do not burn white.
            for (var i = 0; i < light.Length; i++)
            {
                var c = light[i];
                var peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                if (peak > MaxPool) light[i] = new Color(c.r * MaxPool / peak, c.g * MaxPool / peak, c.b * MaxPool / peak, c.a);
            }
            // Texture rows: row 0 = the -Z edge in Unity textures, so flip (z above counts from +Z).
            var pixels = new Color[n * n];
            for (var z = 0; z < n; z++)
                Array.Copy(light, z * n, pixels, (n - 1 - z) * n, n);
            var texture = new Texture2D(n, n, TextureFormat.RGBAHalf, false, true);
            texture.SetPixels(pixels);
            texture.Apply();
            var path = $"{folder}/Terrain/{kit}_LightPools.exr";
            File.WriteAllBytes(path, texture.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = n;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            Debug.Log($"Setup: city {kit} light pools from {used} lamps ({path}).");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
        private static Material EnsureTerrainMaterial(string folder, string kit, Texture2D table, int biomeIndex, SurfaceInfo surface)
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
                var masks = surface != null && !string.IsNullOrEmpty(surface.masks) ? SurfaceTexture($"{folder}/{surface.masks}", surface.resolution) : null;
                var normals = surface != null && !string.IsNullOrEmpty(surface.normal) ? SurfaceTexture($"{folder}/{surface.normal}", surface.resolution) : null;
                material.SetTexture("_MaskMap", masks);
                material.SetTexture("_SurfaceNormal", normals);
                // Map size 0 = no surface maps: the shader uses the mesh normal and no masks.
                material.SetFloat("_MapSize", masks != null && normals != null ? surface.size : 0f);
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
                var metal = building.type is "industrial" or "slum";
                // One object per box, all named "Box": numbered names (Box_0, Box_1…) filled the Entities name table
                // (16 384) with the old 24 837 shanty colliders. Several boxes on one object made the subscene bake
                // hang (5 Nov 2026), so keep one each; CityForge keeps the counts low (shanties: one box per row).
                foreach (var info in building.colliders ?? Array.Empty<ColliderInfo>())
                {
                    var box = new GameObject("Box") { isStatic = true };
                    box.transform.SetParent(colliders.transform, false);
                    box.transform.localPosition = Vector(info.centre);
                    box.transform.localRotation = Quaternion.Euler(0f, info.yaw, 0f);
                    box.AddComponent<BoxCollider>().size = Vector(info.size);
                    // Impact sounds: plant and tin shacks ring like metal; other boxes count as concrete (Surfaces.Classify).
                    if (metal) box.AddComponent<SurfaceAuthoring>().Kind = SurfaceKind.Metal;
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
        private static void BuildScene(string folder, Manifest manifest, TerrainData terrain, Dictionary<string, GameObject> prefabs,
            Material buildingMaterial)
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

                PlaceLights(scene, terrain.lights);
                PlaceEmitters(scene, terrain.emitters);
                PlaceAtmosphere(scene, terrain.heightFog);
                PlaceTraffic(scene, folder, manifest.kit, terrain.traffic, terrain.lava, prefabs, buildingMaterial);

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

        /// <summary>Light budget per kind. Tune here by eye. Most = cap for the kind; Merge = lights of the kind closer than
        /// this (m) are one; Range and Brightness = x the CityForge range and intensity. The sum of Most stays under the Forward+ limit (256 visible).</summary>
        private sealed class LightLook
        {
            public string Kind;
            public int Most;
            public float Merge;
            public float Range = 1f;
            public float Brightness = 1f;
        }

        private static readonly LightLook[] LightLooks =
        {
            new() { Kind = "lava", Most = 36, Merge = 100f, Range = 1.8f },
            new() { Kind = "flame", Most = 16, Merge = 20f },
            new() { Kind = "pad", Most = 12, Merge = 14f },
            new() { Kind = "flood", Most = 40, Merge = 40f, Brightness = 2f },
            new() { Kind = "shanty", Most = 24, Merge = 30f },
            new() { Kind = "street", Most = 128, Merge = 22f, Range = 1.4f, Brightness = 3f },
        };

        /// <summary>CityForge intensities x this = URP point light intensity (URP lights fall off with 1/d²; tuned by eye
        /// at night, 5 Nov 2026).</summary>
        private const float LightIntensityScale = 40f;

        /// <summary>"Lights": point lights from the CityForge list. Per kind (<see cref="LightLooks"/>): close lights
        /// merged, then thinned evenly to the kind's cap, so every part of the map keeps some. No shadows (cost);
        /// realtime, so they also light the choppers.</summary>
        private static void PlaceLights(Scene scene, CityLight[] lights)
        {
            if (lights == null || lights.Length == 0) return;
            var root = new GameObject("Lights");
            SceneManager.MoveGameObjectToScene(root, scene);
            var count = 0;
            var report = new List<string>();
            foreach (var look in LightLooks)
            {
                var merged = new List<(Vector3 Position, CityLight Info)>();
                foreach (var info in lights.Where(l => l?.kind == look.Kind && l.position != null && l.position.Length == 3))
                {
                    var position = Vector(info.position);
                    if (merged.Any(m => (m.Position - position).sqrMagnitude < look.Merge * look.Merge)) continue;
                    merged.Add((position, info));
                }
                if (merged.Count == 0) continue;
                var group = new GameObject(look.Kind).transform;
                group.SetParent(root.transform, false);
                // Even thinning: every n-th light (the list follows the roads and walls, so this keeps coverage).
                var keep = Mathf.Min(look.Most, merged.Count);
                for (var k = 0; k < keep; k++)
                {
                    var (position, info) = merged[k * merged.Count / keep];
                    var go = new GameObject($"{look.Kind}_{k}");
                    go.transform.SetParent(group, false);
                    go.transform.localPosition = position;
                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = ColorUtility.TryParseHtmlString(info.colour, out var colour) ? colour : new Color(1f, 0.7f, 0.35f);
                    light.range = Mathf.Max(1f, info.range * look.Range);
                    light.intensity = Mathf.Max(0f, info.intensity * look.Brightness * LightIntensityScale);
                    light.shadows = LightShadows.None;
                    light.lightmapBakeType = LightmapBakeType.Realtime;
                }
                count += keep;
                report.Add($"{look.Kind} {keep}/{merged.Count}");
            }
            Debug.Log($"Setup: placed {count} of {lights.Length} city lights ({string.Join(", ", report)}).");
        }

        // -- steam, smoke, heat ---------------------------------------------------------------------------------------

        /// <summary>Particle look per emitter kind. Tune here (all values in metres and seconds).</summary>
        private sealed class PlumeLook
        {
            public string Kind;
            public int Most;               // most emitters of this kind in a city
            public float Merge;            // emitters of this kind closer than this are one
            public Color Colour;           // rgb, a = most opacity
            public Vector2 Lifetime;
            public Vector2 Speed;
            public Vector2 SizeOfRadius;   // start size as a share of the outlet radius
            public float MinSize;
            public float Grow;             // size at the end of life x start size
            public float RatePerMetre;     // particles per second per metre of outlet radius
            public float MinRate;
            public int MaxParticles;
            public Color Glow;             // emission (linear): city light from below, so plumes show at night
        }

        private static readonly PlumeLook[] PlumeLooks =
        {
            new() { Kind = "steam", Most = 40, Merge = 10f, Colour = new Color(0.86f, 0.87f, 0.89f, 0.55f), Lifetime = new Vector2(14f, 20f),
                    Speed = new Vector2(2.5f, 4f), SizeOfRadius = new Vector2(0.9f, 1.5f), MinSize = 4f, Grow = 3.5f, RatePerMetre = 0.35f,
                    MinRate = 2f, MaxParticles = 140,
                    Glow = new Color(0.16f, 0.12f, 0.09f) },
            new() { Kind = "smoke", Most = 70, Merge = 3f, Colour = new Color(0.18f, 0.17f, 0.16f, 0.6f), Lifetime = new Vector2(10f, 14f),
                    Speed = new Vector2(2f, 3f), SizeOfRadius = new Vector2(2f, 3f), MinSize = 2f, Grow = 4f, RatePerMetre = 1.5f,
                    MinRate = 2.5f, MaxParticles = 70,
                    Glow = new Color(0.03f, 0.022f, 0.016f) },
            new() { Kind = "heat", Most = 20, Merge = 3f, Colour = new Color(0.36f, 0.31f, 0.28f, 0.25f), Lifetime = new Vector2(3f, 5f),
                    Speed = new Vector2(2.5f, 3.5f), SizeOfRadius = new Vector2(2f, 3.5f), MinSize = 1.5f, Grow = 2.5f, RatePerMetre = 4f,
                    MinRate = 3f, MaxParticles = 30,
                    Glow = new Color(0.5f, 0.14f, 0.03f) },
        };

        /// <summary>One wind for every plume (world, m/s): plumes lean the same way.</summary>
        private static readonly Vector3 Wind = new(1.6f, 0f, 0.7f);
        private static string PlumeMaterialPath(string kind) => $"Assets/Chopper/Data/Effects/CityPlume_{kind}.mat";

        /// <summary>"Emitters": a looping world-space particle system on every kept outlet (a cap and a merge distance per
        /// kind). Lit particles, so lava, street lights and flares colour the steam at night. Particle systems in a
        /// SubScene stay GameObjects (companion objects).</summary>
        private static void PlaceEmitters(Scene scene, CityEmitter[] emitters)
        {
            if (emitters == null || emitters.Length == 0) return;
            var root = new GameObject("Emitters");
            SceneManager.MoveGameObjectToScene(root, scene);
            foreach (var look in PlumeLooks)
            {
                var material = EnsurePlumeMaterial(look);
                if (material == null) return;
                var group = new GameObject(look.Kind).transform;
                group.SetParent(root.transform, false);
                var kept = new List<Vector3>();
                foreach (var info in emitters.Where(e => e?.kind == look.Kind && e.position != null && e.position.Length == 3)
                             .OrderByDescending(e => e.radius))
                {
                    if (kept.Count >= look.Most) break;
                    var position = Vector(info.position);
                    if (kept.Any(p => (p - position).sqrMagnitude < look.Merge * look.Merge)) continue;
                    kept.Add(position);
                    var go = new GameObject($"{look.Kind}_{kept.Count}");
                    go.transform.SetParent(group, false);
                    go.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(-90f, 0f, 0f));   // cone points up
                    MakePlume(go, look, Mathf.Max(0.3f, info.radius), material);
                }
            }
        }

        private static void MakePlume(GameObject go, PlumeLook look, float radius, Material material)
        {
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = look.MaxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(look.Lifetime.x, look.Lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(look.Speed.x, look.Speed.y);
            var size = Mathf.Max(look.MinSize, radius * look.SizeOfRadius.x);
            main.startSize = new ParticleSystem.MinMaxCurve(size, Mathf.Max(size * 1.2f, radius * look.SizeOfRadius.y));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(look.Colour, look.Colour * new Color(0.9f, 0.9f, 0.9f, 0.8f));
            main.gravityModifier = -0.01f;

            var emission = system.emission;
            emission.rateOverTime = Mathf.Max(look.MinRate, radius * look.RatePerMetre);

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = radius;

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            // All three axes in the same curve mode (two constants), or Unity rejects them.
            velocity.x = new ParticleSystem.MinMaxCurve(Wind.x * 0.6f, Wind.x * 1.2f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0.3f);
            velocity.z = new ParticleSystem.MinMaxCurve(Wind.z * 0.6f, Wind.z * 1.2f);

            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(Wind.magnitude * 1.5f + 1f);
            limit.dampen = 0.05f;

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.6f);
            noise.frequency = 0.15f;
            noise.scrollSpeed = 0.2f;

            var growth = system.sizeOverLifetime;
            growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, look.Grow));

            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
            var colourOverLifetime = system.colorOverLifetime;
            colourOverLifetime.enabled = true;
            colourOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = 4f;
        }

        /// <summary>Plumes: URP Particles Simple Lit (lit by the city lights), transparent alpha blend, Unity's soft round
        /// particle texture, soft particles. Made once.</summary>
        /// <summary>One particle material per plume kind (its glow differs). Made once, then left alone for tuning.</summary>
        private static Material EnsurePlumeMaterial(PlumeLook look)
        {
            var path = PlumeMaterialPath(look.Kind);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("Setup: URP particle shaders not found; no city steam and smoke.");
                return null;
            }
            material = new Material(shader) { name = $"CityPlume_{look.Kind}" };
            material.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SoftParticlesEnabled", 1f);
            material.SetVector("_SoftParticleFadeParams", new Vector4(0f, 4f, 0f, 0f));
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_SOFTPARTICLES_ON");
            material.SetColor("_EmissionColor", look.Glow);
            material.EnableKeyword("_EMISSION");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(material, path);
            // Set the keywords again on the asset: before CreateAsset they were lost (seen 5 Nov 2026, no glow).
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_SOFTPARTICLES_ON");
            material.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            return material;
        }

        // -- reflection probes, fog -----------------------------------------------------------------------------------

        /// <summary>
        /// "Reflection Probes": a 3 x 3 grid of baked box probes over the city floor (glass, wet ground and puddles
        /// reflect the neon) and one high probe for the tower tops. Probes use the cubemaps step 10 baked for the map's
        /// default time of day when they exist (<see cref="MapSceneSetup.ProbePath"/>); otherwise they stay plain baked
        /// probes. Placed by step 10 in the match scene, not in the city SubScene: in a SubScene they are companion
        /// objects, made again on each domain reload, and URP then culls a destroyed probe for a frame
        /// (NullReferenceException in ReflectionProbeManager.UpdateGpuData, then ZBinningJob errors).
        /// </summary>
        internal static void PlaceProbes(Scene scene, float mapSize, string kit)
        {
            var root = new GameObject("Reflection Probes") { isStatic = true };
            SceneManager.MoveGameObjectToScene(root, scene);
            var spacing = Mathf.Clamp(mapSize * 0.15f, 250f, 500f);
            for (var i = -1; i <= 1; i++)
            for (var j = -1; j <= 1; j++)
                AddProbe(root.transform, kit, $"Probe_{i + 1}{j + 1}", new Vector3(i * spacing, 35f, j * spacing), new Vector3(spacing, 160f, spacing));
            AddProbe(root.transform, kit, "Probe_High", new Vector3(0f, 220f, 0f), new Vector3(spacing * 3f, 300f, spacing * 3f));
        }

        private static void AddProbe(Transform parent, string kit, string name, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name) { isStatic = true };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
            probe.size = size;
            probe.boxProjection = true;
            probe.resolution = 256;
            probe.hdr = true;
            probe.importance = name == "Probe_High" ? 0 : 1;
            var baked = AssetDatabase.LoadAssetAtPath<Texture>(MapSceneSetup.ProbePath(kit, MapSceneSetup.DefaultTime, name));
            if (baked != null)
            {
                probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
                probe.customBakedTexture = baked;
            }
        }

        /// <summary>"Atmosphere": the map's height fog (CityAtmosphereAuthoring, baked to an ECS component the client
        /// hands to the full-screen fog pass; Setup step 9 adds that pass to the renderer).</summary>
        private static void PlaceAtmosphere(Scene scene, HeightFogInfo fog)
        {
            if (fog == null || fog.density <= 0f) return;
            var go = new GameObject("Atmosphere");
            SceneManager.MoveGameObjectToScene(go, scene);
            var authoring = go.AddComponent<Chopper.Simulation.Visuals.CityAtmosphereAuthoring>();
            var colour = ColorUtility.TryParseHtmlString(fog.colour, out var parsed) ? parsed : new Color(0.29f, 0.24f, 0.22f);
            authoring.Set(colour, fog.density, fog.@base, fog.falloff, fog.most);
        }

        private const string TrafficSettingsPath = "Assets/Chopper/Data/Rendering/TrafficSettings.asset";
        private static readonly string[] TrafficModels = { "Traffic_Car", "Traffic_Truck", "Traffic_Flyer", "Traffic_Drone" };

        /// <summary>"Traffic": the map's cosmetic traffic (CityTrafficAuthoring: paths from "traffic", the vehicle models'
        /// LOD0 meshes, an instanced copy of the building material, the shared TrafficSettings). The client's
        /// CityTrafficSystem moves and draws the vehicles. The vehicle models are in the manifest (not placed).</summary>
        private static void PlaceTraffic(Scene scene, string folder, string kit, TrafficInfo traffic, LavaInfo[] lava,
            Dictionary<string, GameObject> prefabs, Material buildingMaterial)
        {
            if (traffic == null || buildingMaterial == null) return;
            var paths = new List<Chopper.Simulation.Visuals.CityTrafficAuthoring.PathData>();
            foreach (var info in (traffic.lanes ?? Array.Empty<TrafficPathInfo>()).Concat(traffic.air ?? Array.Empty<TrafficPathInfo>()))
            {
                if (info?.points == null || info.points.Length < 6) continue;
                if (!Enum.TryParse<Chopper.Simulation.Visuals.TrafficKind>(info.kind, ignoreCase: true, out var kind)) continue;
                var points = new Vector3[info.points.Length / 3];
                for (var i = 0; i < points.Length; i++)
                    points[i] = new Vector3(info.points[i * 3], info.points[i * 3 + 1], info.points[i * 3 + 2]);
                var stops = new Vector2[(info.stops?.Length ?? 0) / 2];
                for (var i = 0; i < stops.Length; i++) stops[i] = new Vector2(info.stops[i * 2], info.stops[i * 2 + 1]);
                paths.Add(new Chopper.Simulation.Visuals.CityTrafficAuthoring.PathData
                    { kind = kind, closed = info.closed, speed = info.speed, points = points, stops = stops });
            }
            var lights = (traffic.searchlights ?? Array.Empty<SearchlightInfo>())
                .Where(l => l?.position is { Length: >= 3 })
                .Select(l => new Chopper.Simulation.Visuals.CityTrafficAuthoring.SearchlightData
                {
                    position = Vector(l.position),
                    phase = l.phase,
                    speed = l.speed,
                    colour = ColorUtility.TryParseHtmlString(l.colour, out var parsed) ? parsed : Color.white,
                })
                .ToList();
            var cranes = (traffic.cranes?.items ?? Array.Empty<CraneItem>())
                .Where(c => c?.pivot is { Length: >= 3 })
                .Select(c => new Chopper.Simulation.Visuals.CityTrafficAuthoring.CraneData
                    { pivot = Vector(c.pivot), phase = c.phase, speed = c.speed, swing = c.swing })
                .ToList();
            var jib = prefabs.TryGetValue("Crane_Jib", out var jibPrefab) ? Lod0Mesh(jibPrefab) : null;
            var heat = (lava ?? Array.Empty<LavaInfo>())
                .Where(l => l?.points != null && l.points.Length >= 6)
                .Select(l => new Chopper.Simulation.Visuals.CityTrafficAuthoring.LavaData
                {
                    width = l.width,
                    points = Enumerable.Range(0, l.points.Length / 3)
                        .Select(i => new Vector3(l.points[i * 3], l.points[i * 3 + 1], l.points[i * 3 + 2])).ToArray(),
                })
                .ToList();
            var meshes = TrafficModels.Select(name => prefabs.TryGetValue(name, out var prefab) ? Lod0Mesh(prefab) : null).ToArray();
            if (paths.Count == 0 || meshes[0] == null)
            {
                Debug.LogWarning($"Setup: city {kit} has no traffic paths or no Traffic_Car model. Export the city again from CityForge.");
                return;
            }
            var go = new GameObject("Traffic");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<Chopper.Simulation.Visuals.CityTrafficAuthoring>().Set(EnsureTrafficSettings(), meshes[0], meshes[1], meshes[2],
                meshes[3], EnsureTrafficMaterial(folder, kit, buildingMaterial),
                EnsureBeamMaterial(TrailMaterialPath, new Color(3.2f, 0.8f, 1.7f), facing: 0f, falloff: 1.2f, soft: 0.5f, near: 2f),
                EnsureBeamMaterial(BeamMaterialPath, new Color(0.22f, 0.24f, 0.28f), facing: 1f, falloff: 1.6f, soft: 6f, near: 25f),
                paths, lights, jib, cranes, traffic.cranes?.floods ?? Array.Empty<float>(),
                heat.Count > 0 ? EnsureHazeMaterial() : null, heat);
        }

        private const string HazeShader = "Chopper/HeatHaze";
        private const string HazeMaterialPath = "Assets/Chopper/Data/Rendering/HeatHaze.mat";

        /// <summary>Heat shimmer over the lava (Art/Shaders/HeatHaze.shader; needs the URP opaque texture, which
        /// step 9 turns on). Made once, then left alone for tuning.</summary>
        private static Material EnsureHazeMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(HazeMaterialPath);
            if (material != null) return material;
            var shader = Shader.Find(HazeShader);
            if (shader == null)
            {
                Debug.LogWarning($"Setup: shader '{HazeShader}' not found (Art/Shaders/HeatHaze.shader does not compile?). No heat shimmer.");
                return null;
            }
            material = new Material(shader) { name = "HeatHaze" };
            Directory.CreateDirectory(Path.GetDirectoryName(HazeMaterialPath));
            AssetDatabase.CreateAsset(material, HazeMaterialPath);
            return material;
        }

        private const string LightBeamShader = "Chopper/LightBeam";
        private const string TrailMaterialPath = "Assets/Chopper/Data/Rendering/TrafficTrail.mat";
        private const string BeamMaterialPath = "Assets/Chopper/Data/Rendering/SearchlightBeam.mat";

        /// <summary>Additive LightBeam material (Art/Shaders/LightBeam.shader), instanced: made once, then left alone
        /// for tuning. Null when the shader is missing (then no trails or beams).</summary>
        private static Material EnsureBeamMaterial(string path, Color colour, float facing, float falloff, float soft, float near)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find(LightBeamShader);
            if (shader == null)
            {
                Debug.LogWarning($"Setup: shader '{LightBeamShader}' not found (Art/Shaders/LightBeam.shader does not compile?). " +
                                 "No traffic trails or searchlight beams.");
                return null;
            }
            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path), enableInstancing = true };
            material.SetColor("_Colour", colour);
            material.SetFloat("_Facing", facing);
            material.SetFloat("_Falloff", falloff);
            material.SetFloat("_Soft", soft);
            material.SetFloat("_Near", near);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Mesh Lod0Mesh(GameObject prefab) => prefab.GetComponentsInChildren<MeshFilter>(true)
            .Where(f => f.sharedMesh != null)
            .OrderBy(f => f.name.EndsWith("_LOD0", StringComparison.Ordinal) ? 0 : 1)
            .Select(f => f.sharedMesh)
            .FirstOrDefault();

        /// <summary>The building material with GPU instancing on (traffic is drawn instanced). Follows the building
        /// material: its properties are copied again on every import.</summary>
        private static Material EnsureTrafficMaterial(string folder, string kit, Material building)
        {
            var path = $"{folder}/Materials/{kit}_Traffic.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(building) { name = $"{kit}_Traffic" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = building.shader;
            material.CopyPropertiesFromMaterial(building);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Shared traffic numbers: made once, then left alone for tuning.</summary>
        private static Chopper.Simulation.Visuals.TrafficSettings EnsureTrafficSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<Chopper.Simulation.Visuals.TrafficSettings>(TrafficSettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<Chopper.Simulation.Visuals.TrafficSettings>();
            Directory.CreateDirectory(Path.GetDirectoryName(TrafficSettingsPath));
            AssetDatabase.CreateAsset(settings, TrafficSettingsPath);
            return settings;
        }

        private static void SetStatic(GameObject root)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.isStatic = true;
        }
    }
}
