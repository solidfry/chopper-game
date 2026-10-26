using System.Collections.Generic;
using System.Linq;
using Chopper.Simulation.Visuals;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Copies the old chopper's look (Prefabs/Chopper/ChopperNetworked) onto the ghost: material overrides on the Cobra
    /// model, rotor spin (from its SciFiRotation settings), the spot light and the smoke effects. Parts are matched
    /// through the model asset both prefabs use, so names do not matter. Adds only what is missing.
    /// </summary>
    internal static class LegacyChopperVisuals
    {
        public const string LegacyPrefabPath = "Assets/Prefabs/Chopper/ChopperNetworked.prefab";
        private const string SmokePrefabName = "Smoke_Small_Light_FX";

        /// <param name="ghostRoot">Root of the opened ghost prefab contents.</param>
        public static void Apply(GameObject ghostRoot)
        {
            var legacy = AssetDatabase.LoadAssetAtPath<GameObject>(LegacyPrefabPath);
            if (legacy == null)
            {
                Debug.LogWarning($"Setup: no old chopper at {LegacyPrefabPath}; skipped its materials, rotors and lights.");
                return;
            }

            var ghostParts = PartsBySource(ghostRoot);
            var materials = CopyMaterials(legacy, ghostParts);
            var rotors = CopyRotors(legacy, ghostRoot, ghostParts);
            var extras = CopyLightsAndSmoke(legacy.transform, ghostRoot.transform);
            Debug.Log($"Setup: old chopper look copied ({materials} material override(s), {rotors} rotor(s), {extras} light/smoke object(s)).");
        }

        /// <summary>Ghost transforms keyed by the model-asset object they come from.</summary>
        private static Dictionary<Object, Transform> PartsBySource(GameObject root)
        {
            var parts = new Dictionary<Object, Transform>();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(transform.gameObject);
                if (source != null && source != transform.gameObject) parts.TryAdd(source, transform);
            }
            return parts;
        }

        private static Transform Match(Component legacyComponent, Dictionary<Object, Transform> ghostParts)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(legacyComponent.gameObject);
            return source != null && ghostParts.TryGetValue(source, out var part) ? part : null;
        }

        private static int CopyMaterials(GameObject legacy, Dictionary<Object, Transform> ghostParts)
        {
            var copied = 0;
            foreach (var legacyRenderer in legacy.GetComponentsInChildren<Renderer>(true))
            {
                var part = Match(legacyRenderer, ghostParts);
                var renderer = part != null ? part.GetComponent<Renderer>() : null;
                if (renderer == null || renderer.sharedMaterials.SequenceEqual(legacyRenderer.sharedMaterials)) continue;
                renderer.sharedMaterials = legacyRenderer.sharedMaterials;
                copied++;
            }
            return copied;
        }

        private static int CopyRotors(GameObject legacy, GameObject ghostRoot, Dictionary<Object, Transform> ghostParts)
        {
            var visuals = ghostRoot.GetComponent<ChopperVisualsAuthoring>();
            if (visuals == null) visuals = ghostRoot.AddComponent<ChopperVisualsAuthoring>();
            if (visuals.Rotors.Length > 0) return 0;

            var rotors = new List<ChopperVisualsAuthoring.Rotor>();
            foreach (var spinner in legacy.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null && c.GetType().Name == "SciFiRotation"))
            {
                var part = Match(spinner, ghostParts);
                var rate = new SerializedObject(spinner).FindProperty("rotateVector");
                if (part == null || rate == null) continue;
                rotors.Add(new ChopperVisualsAuthoring.Rotor { Part = part, DegreesPerSecond = rate.vector3Value });
            }

            visuals.SetRotors(rotors.ToArray());
            return rotors.Count;
        }

        /// <summary>
        /// Realtime lights (area lights are bake-only, so skipped) and smoke effects, at the same place relative to the root.
        /// </summary>
        private static int CopyLightsAndSmoke(Transform legacyRoot, Transform ghostRoot)
        {
            var copied = 0;
            foreach (var light in legacyRoot.GetComponentsInChildren<Light>(true))
            {
                if (light.type == LightType.Rectangle || light.type == LightType.Disc) continue;
                if (ghostRoot.Find(light.name) != null) continue;
                Place(Object.Instantiate(light.gameObject, ghostRoot), light.transform, legacyRoot);
                copied++;
            }

            var smokeIndex = 0;
            foreach (var transform in legacyRoot.GetComponentsInChildren<Transform>(true))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(transform.gameObject);
                if (source == null || source.name != SmokePrefabName || !PrefabUtility.IsAnyPrefabInstanceRoot(transform.gameObject)) continue;

                var name = $"{SmokePrefabName} {smokeIndex++}";
                if (ghostRoot.Find(name) != null) continue;
                var smoke = (GameObject)PrefabUtility.InstantiatePrefab(source, ghostRoot);
                Place(smoke, transform, legacyRoot);
                smoke.name = name;
                copied++;
            }
            return copied;
        }

        /// <summary>Copy is already a child of the ghost root; give it the legacy object's pose relative to the legacy root.</summary>
        private static void Place(GameObject copy, Transform legacy, Transform legacyRoot)
        {
            copy.name = copy.name.Replace("(Clone)", "").Trim();
            var localPosition = legacyRoot.InverseTransformPoint(legacy.position);
            var localRotation = Quaternion.Inverse(legacyRoot.rotation) * legacy.rotation;
            copy.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            copy.transform.localScale = legacy.lossyScale;
        }
    }
}
