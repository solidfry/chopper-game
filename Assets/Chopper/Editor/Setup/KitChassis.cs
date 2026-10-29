using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chopper.Presentation.Config;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Loadout;
using Chopper.Simulation.Visuals;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// "Build chassis from kit" (plan section 5). One chopper ghost per chassis from the default kit's model: a prefab
    /// variant of the placeholder ghost (same ghost, physics and flight setup) with the kit model in place of the old
    /// one, a <see cref="SocketAuthoring"/> on each socket marker of the model, body colliders that follow the model
    /// (<see cref="FitColliders"/>) and the chassis's default loadout. Links the chassis to it, and the kit's module models to their modules (rotors also get
    /// a spin). Creates only what is missing: an existing prefab only gets missing sockets; a chassis or module linked
    /// to art other than the placeholders keeps it.
    /// </summary>
    internal static class KitChassis
    {
        public const string Folder = "Assets/Chopper/Prefabs/Chassis";
        private const string ModelName = "Model";
        private const string PlaceholderSocketsName = "Sockets";
        private const string CollidersName = "Colliders";
        /// <summary>The body is cut into this many slices along its length; each gets a box.</summary>
        private const int ColliderSlices = 4;
        /// <summary>Thin parts (masts, antennas, aerials) are left out of the boxes: this share of vertices on each side.</summary>
        private const float ColliderTrim = 0.015f;
        /// <summary>Rotor blades, degrees per second about the hub (local Y). Tune it on the rotor modules.</summary>
        private static readonly Vector3 RotorSpin = new(0f, 1500f, 0f);

        public static void EnsureAll(GameConfig config)
        {
            var catalog = config != null ? config.LoadoutCatalog : null;
            var ghost = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabSetup.ChopperPrefabPath);
            var manifest = KitAssets.LoadKit(KitAssets.DefaultKit, out var folder);
            if (catalog == null || ghost == null || manifest?.parts == null)
            {
                Debug.LogWarning("Setup: building chassis from the kit needs the loadout catalog, the chopper prefab and " +
                                 $"kit {KitAssets.DefaultKit}. Run steps 1, 2 and 5 first.");
                return;
            }

            foreach (var part in manifest.parts.Where(p => p.kind == "chassis"))
            {
                var chassis = ChassisFor(catalog, part);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{part.file}");
                if (chassis == null || model == null)
                {
                    Debug.LogWarning($"Setup: kit part {part.name} has no chassis asset of the same name, or its model is missing.");
                    continue;
                }

                var prefab = EnsurePrefab(ghost, model, part, chassis);
                if (prefab == null || (chassis.Prefab != null && chassis.Prefab != ghost)) continue;
                chassis.SetDefaults(prefab, null);
                EditorUtility.SetDirty(chassis);
            }

            LinkModules(catalog, manifest, folder);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Problems for the Validate list: a chassis that still flies the placeholder, a module without kit art.</summary>
        public static IEnumerable<(string Message, Object Context)> Problems(GameConfig config)
        {
            var catalog = config != null ? config.LoadoutCatalog : null;
            var manifest = KitAssets.LoadKit(KitAssets.DefaultKit, out _);
            if (catalog == null || manifest?.parts == null) yield break;
            var ghost = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabSetup.ChopperPrefabPath);

            foreach (var part in manifest.parts)
            {
                if (part.kind == "chassis")
                {
                    var chassis = ChassisFor(catalog, part);
                    if (chassis == null)
                        yield return ($"Kit {manifest.kit}: no chassis asset named {part.name} in the loadout catalog.", catalog);
                    else if (chassis.Prefab == null || chassis.Prefab == ghost)
                        yield return ($"{chassis.name} flies the placeholder chopper. Press '6. Build chassis from kit'.", chassis);
                }
                else if (part.moduleIds != null)
                {
                    foreach (var module in Modules(catalog, part).Where(m => IsPlaceholder(m.Visual)))
                        yield return ($"{module.name} has no kit model ({part.name}). Press '6. Build chassis from kit'.", module);
                }
            }
        }

        /// <summary>Kit chassis parts match chassis assets by name (Chassis_Gunship).</summary>
        private static ChassisDefinition ChassisFor(LoadoutCatalog catalog, KitAssets.PartInfo part) =>
            catalog.Chassis.FirstOrDefault(c => c != null && c.name == part.name);

        private static IEnumerable<ModuleDefinition> Modules(LoadoutCatalog catalog, KitAssets.PartInfo part) =>
            catalog.Modules.Where(m => m != null && part.moduleIds.Contains(m.Id));

        private static string PrefabPath(ChassisDefinition chassis) => $"{Folder}/Chopper_{chassis.DisplayName}.prefab";

        private static GameObject EnsurePrefab(GameObject ghost, GameObject model, KitAssets.PartInfo part, ChassisDefinition chassis)
        {
            var path = PrefabPath(chassis);
            var created = AssetDatabase.LoadAssetAtPath<GameObject>(path) == null;
            if (created)
            {
                Directory.CreateDirectory(Folder);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(ghost);
                try { PrefabUtility.SaveAsPrefabAsset(instance, path); }
                finally { Object.DestroyImmediate(instance); }
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (created) Build(root, model, chassis);
                var added = AddSockets(root, part);
                if (created) FitEffects(root);
                var fitted = FitColliders(root);
                if (!created && added == 0 && !fitted) return AssetDatabase.LoadAssetAtPath<GameObject>(path);

                var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log(created ? $"Setup: made {path} from kit model {model.name}."
                    : $"Setup: {path}: {added} socket(s) added{(fitted ? ", body colliders fitted" : "")}.");
                return saved;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>A new variant: the kit model replaces the old model and its placeholder sockets.</summary>
        private static void Build(GameObject root, GameObject model, ChassisDefinition chassis)
        {
            foreach (var name in new[] { ModelName, PlaceholderSocketsName })
            {
                var old = root.transform.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.name = ModelName;

            // The old model's blades are gone: rotor modules spin now (ModuleDefinition.VisualSpin).
            if (root.TryGetComponent<ChopperVisualsAuthoring>(out var visuals)) visuals.SetRotors(Array.Empty<ChopperVisualsAuthoring.Rotor>());
            if (root.TryGetComponent<ChopperAuthoring>(out var authoring)) authoring.SetLoadout(chassis, chassis.DefaultModules.ToArray());
        }

        /// <summary>
        /// Body colliders that follow the model: the body is cut into <see cref="ColliderSlices"/> slices along its length
        /// and each slice gets a box round its vertices (thin parts trimmed), so hits and crashes match the shape (the
        /// Lifter's masts and the tail boom are not one big box). They replace the single root box. Unity Physics bakes
        /// the child boxes into one compound collider. False when the prefab has them already.
        /// </summary>
        private static bool FitColliders(GameObject root)
        {
            var model = root.transform.Find(ModelName);
            if (model == null || root.transform.Find(CollidersName) != null) return false;

            var points = new List<Vector3>();
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var toRoot = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (var vertex in filter.sharedMesh.vertices) points.Add(toRoot.MultiplyPoint3x4(vertex));
            }
            if (points.Count == 0) return false;

            var holder = new GameObject(CollidersName);
            holder.transform.SetParent(root.transform, false);
            var minZ = points.Min(p => p.z);
            var length = points.Max(p => p.z) - minZ;
            for (var slice = 0; slice < ColliderSlices; slice++)
            {
                var from = minZ + length * slice / ColliderSlices;
                var to = minZ + length * (slice + 1) / ColliderSlices;
                var inSlice = points.Where(p => p.z >= from && (p.z < to || slice == ColliderSlices - 1)).ToList();
                if (inSlice.Count < 8) continue;
                var (lowX, highX) = Trimmed(inSlice.Select(p => p.x));
                var (lowY, highY) = Trimmed(inSlice.Select(p => p.y));
                var box = holder.AddComponent<BoxCollider>();
                box.center = new Vector3((lowX + highX) / 2f, (lowY + highY) / 2f, (from + to) / 2f);
                box.size = new Vector3(Mathf.Max(0.2f, highX - lowX), Mathf.Max(0.2f, highY - lowY), to - from);
            }

            if (root.TryGetComponent<BoxCollider>(out var single)) Object.DestroyImmediate(single);
            return true;
        }

        private static (float Low, float High) Trimmed(IEnumerable<float> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var cut = (int)(sorted.Count * ColliderTrim);
            return (sorted[cut], sorted[sorted.Count - 1 - cut]);
        }

        /// <summary>A socket on every marker node of the model (SOCKET_Pylon_1) that has none.</summary>
        private static int AddSockets(GameObject root, KitAssets.PartInfo part)
        {
            var model = root.transform.Find(ModelName);
            if (model == null || part.sockets == null) return 0;

            var nodes = model.GetComponentsInChildren<Transform>(true);
            var added = 0;
            foreach (var info in part.sockets)
            {
                var marker = nodes.FirstOrDefault(t => t.name == info.name);
                if (marker == null || !Enum.TryParse<SlotType>(info.slot, out var slot))
                {
                    Debug.LogWarning($"Setup: {part.name} has no marker {info.name}, or its slot {info.slot} is unknown.");
                    continue;
                }
                if (marker.GetComponent<SocketAuthoring>() != null) continue;
                marker.gameObject.AddComponent<SocketAuthoring>().Initialise(slot, info.index, SocketAssets.FocusYaw(slot, info.index));
                added++;
            }
            return added;
        }

        /// <summary>
        /// The old model's nose lights and exhaust smoke (inherited) move to this model's nose and engine sockets. The
        /// lights move together, so they keep their layout.
        /// </summary>
        private static void FitEffects(GameObject root)
        {
            var sockets = root.GetComponentsInChildren<SocketAuthoring>(true);
            var nose = sockets.FirstOrDefault(s => s.Slot == SlotType.Nose);
            var engine = sockets.FirstOrDefault(s => s.Slot == SlotType.Engine);

            var lights = root.GetComponentsInChildren<Light>(true).Where(l => l.transform.parent == root.transform).ToArray();
            var spot = lights.FirstOrDefault(l => l.type == LightType.Spot);
            if (nose != null && spot != null)
            {
                var shift = Local(root, nose.transform) + new Vector3(0f, 0.3f, 1.3f) - spot.transform.localPosition;
                foreach (var light in lights) light.transform.localPosition += shift;
            }

            if (engine == null) return;
            var smoke = root.GetComponentsInChildren<ParticleSystem>(true).Where(p => p.transform.parent == root.transform).ToArray();
            for (var i = 0; i < smoke.Length; i++)
                smoke[i].transform.localPosition = Local(root, engine.transform) + new Vector3(i % 2 == 0 ? 0.3f : -0.3f, 0f, -0.6f);
        }

        private static Vector3 Local(GameObject root, Transform target) => root.transform.InverseTransformPoint(target.position);

        /// <summary>Bounds of the model's meshes in root space.</summary>
        private static Bounds ModelBounds(Transform root, GameObject model)
        {
            var bounds = new Bounds();
            var first = true;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var mesh = filter.sharedMesh.bounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var sign = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    var point = toRoot.MultiplyPoint3x4(mesh.center + Vector3.Scale(mesh.extents, sign));
                    if (first) bounds = new Bounds(point, Vector3.zero);
                    else bounds.Encapsulate(point);
                    first = false;
                }
            }
            return bounds;
        }

        /// <summary>Kit models replace missing or placeholder module art; rotors that have no spin get one; weapons
        /// that have no muzzle get the model's barrel tip.</summary>
        private static void LinkModules(LoadoutCatalog catalog, KitAssets.Manifest manifest, string folder)
        {
            var linked = 0;
            foreach (var part in manifest.parts.Where(p => p.kind == "module" && p.moduleIds != null))
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{part.file}");
                if (model == null) continue;
                foreach (var module in Modules(catalog, part))
                {
                    var changed = false;
                    if (IsPlaceholder(module.Visual))
                    {
                        module.SetVisual(model);
                        linked++;
                        changed = true;
                    }
                    if (module.Visual == model && module.Slot == SlotType.Rotor && module.VisualSpin == Vector3.zero)
                    {
                        module.SetVisualSpin(RotorSpin);
                        changed = true;
                    }
                    if (module.Visual == model && module.Weapon != null && module.Muzzle == Vector3.zero)
                    {
                        module.SetMuzzle(BarrelTip(model));
                        changed = true;
                    }
                    if (changed) EditorUtility.SetDirty(module);
                }
            }
            if (linked > 0) Debug.Log($"Setup: linked kit {manifest.kit} models to {linked} module(s).");
        }

        /// <summary>Front centre of the model's bounds (kit modules point +Z from the socket): where shots start.</summary>
        private static Vector3 BarrelTip(GameObject model)
        {
            var bounds = ModelBounds(model.transform, model);
            return new Vector3(bounds.center.x, bounds.center.y, bounds.max.z);
        }

        /// <summary>No visual, or placeholder art: kit art may replace it. Other art was chosen by hand and stays.</summary>
        private static bool IsPlaceholder(GameObject visual) =>
            visual == null || AssetDatabase.GetAssetPath(visual).StartsWith(SocketAssets.ArtFolder);
    }
}
