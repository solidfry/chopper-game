using System.Linq;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Match;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Builds the pad turret ghost prefab from the PartForge kit (step 5): base (root, on the ground), head (yaw pivot,
    /// <see cref="YawHeight"/> up) and guns (pitch pivot, <see cref="PitchHeight"/> above the head). Heights match
    /// PartForge's <c>TURRET_YAW_HEIGHT</c> and <c>TURRET_PITCH_HEIGHT</c>. A box collider over base and head takes the
    /// hits. A pre-spawned ghost (interpolated, static optimisation) like the training target.
    /// </summary>
    public static class TurretAssets
    {
        public const string PrefabPath = "Assets/Chopper/Prefabs/PadTurret.prefab";
        private const string Kit = "Kit02";
        private const float YawHeight = 1.2f;
        private const float PitchHeight = 1.3f;

        public static GameObject EnsurePrefab()
        {
            var folder = $"{KitAssets.KitsFolder}/{Kit}/Models";
            var basePart = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/Prop_Turret_Base.fbx");
            var headPart = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/Prop_Turret_Head.fbx");
            var gunsPart = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/Prop_Turret_Guns.fbx");
            if (basePart == null || headPart == null || gunsPart == null)
            {
                Debug.LogWarning($"Setup: turret models missing in {folder}. Export the kit from PartForge.");
                return null;
            }

            var ghostType = TypeCache.GetTypesDerivedFrom<MonoBehaviour>().FirstOrDefault(t => t.Name == "GhostAuthoringComponent");
            if (ghostType == null)
            {
                Debug.LogError("Setup: GhostAuthoringComponent type not found. Is Netcode for Entities installed?");
                return null;
            }

            var root = new GameObject("PadTurret");
            try
            {
                Model(basePart, root.transform, Vector3.zero);
                var head = new GameObject("Head").transform;
                head.SetParent(root.transform, false);
                head.localPosition = new Vector3(0f, YawHeight, 0f);
                Model(headPart, head, Vector3.zero);
                var guns = new GameObject("Guns").transform;
                guns.SetParent(head, false);
                guns.localPosition = new Vector3(0f, PitchHeight, 0f);
                Model(gunsPart, guns, Vector3.zero);

                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 1.3f, 0f);
                box.size = new Vector3(3.2f, 2.6f, 3.2f);

                var turret = root.AddComponent<TurretAuthoring>();
                var so = new SerializedObject(turret);
                so.FindProperty("head").objectReferenceValue = head;
                so.FindProperty("guns").objectReferenceValue = guns;
                so.FindProperty("muzzleOffset").vector3Value = new Vector3(0f, YawHeight + PitchHeight, 0f);
                so.ApplyModifiedPropertiesWithoutUndo();

                var ghost = new SerializedObject(root.AddComponent(ghostType));
                SetInt(ghost, "DefaultGhostMode", 0);    // Interpolated
                SetInt(ghost, "SupportedGhostModes", 1); // Interpolated only
                SetInt(ghost, "OptimizationMode", 0);    // Dynamic: the aim changes often
                ghost.ApplyModifiedPropertiesWithoutUndo();

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }


        public const string NukePrefabPath = "Assets/Chopper/Prefabs/Nuke.prefab";

        /// <summary>
        /// The Capture the Nuke nuke: the kit's Prop_Nuke model (origin = hook eye, body below) and a
        /// pre-spawned ghost the server moves (interpolated, dynamic).
        /// </summary>
        public static GameObject EnsureNukePrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitAssets.KitsFolder}/{Kit}/Models/Prop_Nuke.fbx");
            if (model == null)
            {
                Debug.LogWarning("Setup: Prop_Nuke model missing. Export the kit from PartForge.");
                return null;
            }
            var ghostType = TypeCache.GetTypesDerivedFrom<MonoBehaviour>().FirstOrDefault(t => t.Name == "GhostAuthoringComponent");
            if (ghostType == null) return null;

            var root = new GameObject("Nuke");
            try
            {
                Model(model, root.transform, Vector3.zero);
                // No collider: hooking is by distance, and a moved static collider would rebuild the broadphase.
                root.AddComponent<NukeAuthoring>();
                var ghost = new SerializedObject(root.AddComponent(ghostType));
                SetInt(ghost, "DefaultGhostMode", 0);
                SetInt(ghost, "SupportedGhostModes", 1);
                SetInt(ghost, "OptimizationMode", 0);
                ghost.ApplyModifiedPropertiesWithoutUndo();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(NukePrefabPath));
                return PrefabUtility.SaveAsPrefabAsset(root, NukePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Model(GameObject source, Transform parent, Vector3 position)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.transform.SetParent(parent, false);
            model.transform.localPosition = position;
            model.transform.localRotation = Quaternion.identity;
            model.name = source.name;
        }

        private static void SetInt(SerializedObject target, string property, int value)
        {
            var p = target.FindProperty(property);
            if (p != null) p.intValue = value;
        }
    }
}
