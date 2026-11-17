using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Draws each chopper's repair drone from the replicated <see cref="RepairDroneState"/>: it leaves the
    /// pylon, arcs to its target, circles above it with a green repair beam while it heals, and flies back to dock.
    /// The model is <see cref="CombatEffectsSettings.RepairDrone"/> (pooled), or a simple built-in quad drone when empty.
    /// Cosmetic: the server decides the heal.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class RepairDroneEffectsSystem : PresentationSystemBase
    {
        /// <summary>Where the drone docks, in the owner's space (under the tail boom).</summary>
        private static readonly Vector3 DockOffset = new(0f, -0.7f, -1.2f);
        private const float ArcHeight = 1.5f;
        private const float RotorSpin = 2400f;

        private sealed class Drone
        {
            public GameObject Root;
            public LineRenderer Beam;
            public Transform[] Rotors;
            public bool Pooled;
            public float Angle;
            public Vector3 Last;
        }

        private readonly Dictionary<Entity, Drone> _drones = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();
        private readonly Stack<Drone> _spare = new();
        private CombatEffects _effects;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy()
        {
            foreach (var drone in _drones.Values) Destroy(drone);
            foreach (var drone in _spare) Destroy(drone);
            _drones.Clear();
            _spare.Clear();
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            if (_effects != context.Effects)
            {
                foreach (var drone in _drones.Values) Destroy(drone);
                _drones.Clear();
                _effects = context.Effects;
            }
            var settings = _effects != null ? _effects.Settings : null;
            if (settings == null) return;

            var deltaTime = UnityEngine.Time.deltaTime;
            var poses = SystemAPI.GetComponentLookup<LocalToWorld>(true);
            _seen.Clear();
            foreach (var (state, pose, owner) in SystemAPI.Query<RefRO<RepairDroneState>, RefRO<LocalToWorld>>().WithEntityAccess())
            {
                var drone = state.ValueRO;
                if (!drone.Out) continue;
                _seen.Add(owner);
                if (!_drones.TryGetValue(owner, out var visual))
                {
                    visual = Take(settings, (Vector3)pose.ValueRO.Position);
                    _drones[owner] = visual;
                }

                var ownerMatrix = (Matrix4x4)pose.ValueRO.Value;
                var dock = ownerMatrix.MultiplyPoint3x4(DockOffset);
                var hasTarget = drone.Target != Entity.Null && poses.HasComponent(drone.Target);
                var targetCentre = hasTarget ? (Vector3)poses[drone.Target].Position : (Vector3)pose.ValueRO.Position;

                visual.Angle += settings.DroneOrbitSpeed * deltaTime;
                var radians = visual.Angle * Mathf.Deg2Rad;
                var orbit = targetCentre + new Vector3(Mathf.Cos(radians) * settings.DroneOrbitRadius, settings.DroneOrbitHeight,
                    Mathf.Sin(radians) * settings.DroneOrbitRadius);

                var t = drone.PhaseLength > 0f ? Mathf.Clamp01(drone.PhaseTime / drone.PhaseLength) : 1f;
                var eased = t * t * (3f - 2f * t);
                Vector3 position;
                switch (drone.State)
                {
                    case DronePhase.Outbound:
                        position = Vector3.Lerp(dock, orbit, eased) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * ArcHeight);
                        break;
                    case DronePhase.Returning:
                        position = Vector3.Lerp(orbit, dock, eased) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * ArcHeight);
                        break;
                    default:
                        position = orbit;
                        break;
                }

                var root = visual.Root.transform;
                var move = position - visual.Last;
                root.position = position;
                // Face the way it flies, tipped a little forward; while it heals, face the chopper.
                var look = drone.State == DronePhase.Healing ? targetCentre - position : move;
                look.y = 0f;
                if (look.sqrMagnitude > 1e-6f)
                    root.rotation = Quaternion.Slerp(root.rotation, Quaternion.LookRotation(look) * Quaternion.Euler(12f, 0f, 0f),
                        1f - Mathf.Exp(-10f * deltaTime));
                visual.Last = position;
                if (visual.Rotors != null)
                    foreach (var rotor in visual.Rotors)
                        rotor.Rotate(0f, RotorSpin * deltaTime, 0f, Space.Self);

                var healing = drone.State == DronePhase.Healing;
                if (visual.Beam != null)
                {
                    visual.Beam.enabled = healing;
                    if (healing)
                    {
                        var flicker = 0.75f + 0.25f * Mathf.Sin(UnityEngine.Time.time * 31f);
                        var colour = settings.DroneBeamColour * flicker;
                        visual.Beam.startColor = colour;
                        visual.Beam.endColor = new Color(colour.r, colour.g, colour.b, 0.15f);
                        visual.Beam.SetPosition(0, position);
                        visual.Beam.SetPosition(1, targetCentre);
                    }
                }
            }

            _gone.Clear();
            foreach (var owner in _drones.Keys)
                if (!_seen.Contains(owner)) _gone.Add(owner);
            foreach (var owner in _gone)
            {
                Give(_drones[owner]);
                _drones.Remove(owner);
            }
        }

        private Drone Take(CombatEffectsSettings settings, Vector3 at)
        {
            Drone drone;
            if (settings.RepairDrone != null)
            {
                var instance = _effects.SpawnPooled(settings.RepairDrone, at, Quaternion.identity);
                drone = new Drone { Root = instance, Pooled = true, Beam = instance.GetComponentInChildren<LineRenderer>(true) };
            }
            else
            {
                drone = _spare.Count > 0 ? _spare.Pop() : BuildDefault(settings);
                drone.Root.SetActive(true);
                drone.Root.transform.position = at;
            }
            drone.Last = at;
            drone.Angle = Random.Range(0f, 360f);
            if (drone.Beam != null) drone.Beam.enabled = false;
            return drone;
        }

        private void Give(Drone drone)
        {
            if (drone.Pooled)
            {
                if (_effects != null && drone.Root != null) _effects.Release(drone.Root);
                return;
            }
            if (drone.Root == null) return;
            drone.Root.SetActive(false);
            _spare.Push(drone);
        }

        private static void Destroy(Drone drone)
        {
            if (drone.Pooled || drone.Root == null) return;
            Object.Destroy(drone.Root);
        }

        /// <summary>The built-in drone: a flat body, four spinning rotors, a green light and the repair beam.</summary>
        private static Drone BuildDefault(CombatEffectsSettings settings)
        {
            var root = new GameObject("RepairDrone");
            var body = Part(PrimitiveType.Cube, root.transform, Vector3.zero, new Vector3(0.5f, 0.14f, 0.5f), new Color(0.12f, 0.14f, 0.13f));
            Part(PrimitiveType.Cube, body.transform, new Vector3(0f, 0.6f, 0f), new Vector3(0.5f, 0.4f, 0.5f), new Color(0.2f, 1f, 0.5f));
            var rotors = new Transform[4];
            for (var i = 0; i < 4; i++)
            {
                var corner = new Vector3(i % 2 == 0 ? -0.36f : 0.36f, 0.08f, i < 2 ? -0.36f : 0.36f);
                Part(PrimitiveType.Cube, root.transform, corner * 0.5f, new Vector3(0.36f, 0.04f, 0.06f), new Color(0.1f, 0.1f, 0.1f))
                    .transform.localRotation = Quaternion.Euler(0f, i * 90f + 45f, 0f);
                rotors[i] = Part(PrimitiveType.Cylinder, root.transform, corner, new Vector3(0.3f, 0.005f, 0.3f), new Color(0.6f, 0.65f, 0.62f, 0.5f)).transform;
            }

            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.type = LightType.Point;
            light.color = new Color(0.35f, 1f, 0.55f);
            light.range = 6f;
            light.intensity = 3f;
            light.shadows = LightShadows.None;

            var beam = root.AddComponent<LineRenderer>();
            beam.positionCount = 2;
            beam.useWorldSpace = true;
            beam.startWidth = 0.09f;
            beam.endWidth = 0.35f;
            beam.numCapVertices = 2;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.receiveShadows = false;
            // The damage spark material is additive unlit and takes the line colour: tinted green here.
            if (settings.DamageSparkMaterial != null) beam.sharedMaterial = settings.DamageSparkMaterial;
            beam.enabled = false;

            return new Drone { Root = root, Beam = beam, Rotors = rotors };
        }

        private static GameObject Part(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color colour)
        {
            var part = GameObject.CreatePrimitive(type);
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            var renderer = part.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", colour);
            block.SetColor("_Color", colour);
            renderer.SetPropertyBlock(block);
            return part;
        }
    }
}
