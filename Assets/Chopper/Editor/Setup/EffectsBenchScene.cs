using System.IO;
using Chopper.Presentation.Effects;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// The effects bench scene (owner, 19 Nov: "we need a particle/effects testing scene"): flat ground, a few dark
    /// blocks for depth and soft-particle edges, a sun at a match angle, the city post look (bloom, grading), an orbit
    /// camera (<see cref="BenchOrbitCamera"/>) and an <see cref="EffectsBench"/> linked to the game config's effects
    /// settings. No netcode, bots or HUD. Not a game scene and not in the build. Made once; opened when it exists.
    /// </summary>
    internal static class EffectsBenchScene
    {
        public const string ScenePath = "Assets/Chopper/Scenes/EffectsBench.unity";

        /// <summary>Setup window button: asks to save open scenes, makes the scene if missing, opens it.</summary>
        public static void Open()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Effects bench: stop play mode first.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) Build();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var bench = Object.FindAnyObjectByType<EffectsBench>();
            if (bench != null) Selection.activeGameObject = bench.gameObject;
        }

        /// <summary>Makes and saves the scene (replaces an older one). Does not change the open scenes.</summary>
        public static void Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            try
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
                Populate();
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void Populate()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(35f, -40f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.35f, 0.4f, 0.5f);
            RenderSettings.ambientEquatorColor = new Color(0.22f, 0.24f, 0.28f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.08f, 0.1f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(40f, 1f, 40f);
            Paint(ground, new Color(0.18f, 0.19f, 0.21f));
            // Dark blocks like city buildings: smoke edges against walls, fire lighting nearby surfaces.
            Block("Block A", new Vector3(-28f, 12f, 18f), new Vector3(12f, 24f, 12f));
            Block("Block B", new Vector3(30f, 20f, 26f), new Vector3(14f, 40f, 14f));
            Block("Block C", new Vector3(14f, 3f, -16f), new Vector3(8f, 6f, 5f));

            var point = new GameObject("Blast Point").transform;
            var bench = new GameObject("Effects Bench").AddComponent<EffectsBench>();
            var config = ConfigAssets.Load();
            bench.Configure(config != null ? config.Effects : null, point, StandIn());

            var camera = new GameObject("Bench Camera") { tag = "MainCamera" };
            var cam = camera.AddComponent<UnityEngine.Camera>();
            cam.farClipPlane = 3000f;
            cam.nearClipPlane = 0.3f;
            camera.AddComponent<AudioListener>();
            var data = camera.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camera.AddComponent<BenchOrbitCamera>().Configure(point);
            camera.transform.SetPositionAndRotation(new Vector3(-30f, 25f, -50f), Quaternion.Euler(15f, 30f, 0f));

            var volume = new GameObject("Post (city look)").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = CityLookSetup.EnsureProfile();
        }

        /// <summary>The default kit's gunship model (plain art, not the networked prefab); a box when the kit is missing.</summary>
        private static Transform StandIn()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(StandInModel);
            GameObject standIn;
            if (model != null)
            {
                // Name the scene: without it the model can land in the scene that was open before the build.
                standIn = (GameObject)PrefabUtility.InstantiatePrefab(model, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                standIn.name = "Chopper Stand-in";
            }
            else
            {
                standIn = GameObject.CreatePrimitive(PrimitiveType.Cube);
                standIn.name = "Chopper Stand-in (box)";
                standIn.transform.localScale = new Vector3(3f, 2f, 9f);
                Object.DestroyImmediate(standIn.GetComponent<Collider>());
            }
            standIn.transform.position = new Vector3(0f, 10f, 0f);
            return standIn.transform;
        }

        private const string StandInModel = KitAssets.KitsFolder + "/" + KitAssets.DefaultKit + "/Models/Chassis_Gunship.fbx";

        private static void Block(string name, Vector3 position, Vector3 size)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetPositionAndRotation(position, Quaternion.identity);
            block.transform.localScale = size;
            Paint(block, new Color(0.1f, 0.11f, 0.13f));
        }

        /// <summary>A plain URP Lit colour (a scene-only material, not an asset).</summary>
        private static void Paint(GameObject go, Color colour)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;
            var material = new Material(shader) { color = colour };
            material.SetFloat("_Smoothness", 0.15f);
            go.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
