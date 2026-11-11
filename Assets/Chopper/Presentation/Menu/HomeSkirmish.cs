using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Hangar;
using Chopper.Simulation.Loadout;
using UnityEngine;

namespace Chopper.Presentation.Menu
{
    /// <summary>
    /// Home screen life: a few choppers dogfight over the menu city. Each is a chassis model from the loadout catalog
    /// with its default modules on the sockets (rotors spin), drawn in a fine bright version of the city wire material.
    /// They fly figure-eight loops round the city near the camera ring, banking into the turns, and fire bursts of
    /// glowing tracers at the nearest other chopper. Pure decoration: no simulation, no hits. Put it next to
    /// <see cref="HomeFlyover"/>; it starts once the city is up.
    /// </summary>
    public sealed class HomeSkirmish : MonoBehaviour
    {
        private const int TracerPool = 48;

        [SerializeField] private PresentationBridge bridge;
        [SerializeField] private HomeFlyover flyover;
        [SerializeField, Tooltip("City wire material; the choppers use a copy with a finer, brighter grid.")]
        private Material wireMaterial;
        [SerializeField, Tooltip("Tracer material (unlit, bright: bloom makes it glow).")]
        private Material tracerMaterial;
        [SerializeField, Range(0, 12)] private int count = 6;
        [SerializeField, Tooltip("Model scale (bigger than life so they read at menu distance).")]
        private float scale = 5f;
        [SerializeField, Tooltip("Flight speed along the loop, metres per second.")]
        private float speed = 70f;
        [SerializeField] private float tracerSpeed = 700f;
        [SerializeField] private float tracerLength = 45f;
        [SerializeField] private float tracerWidth = 1.6f;

        private sealed class Flyer
        {
            public Transform Root;
            public readonly List<(Transform Part, Vector3 Spin)> Spinners = new();
            public float Phase;
            public float Radius;
            public float Height;
            public float Rate;
            public float NextBurst;
            public int ShotsLeft;
            public float NextShot;
            public Flyer Target;
            public Vector3 Velocity;
        }

        private sealed class Tracer
        {
            public LineRenderer Line;
            public Vector3 Start;
            public Vector3 Direction;
            public float Age;
            public float Life;
        }

        private readonly List<Flyer> _flyers = new();
        private readonly List<Tracer> _tracers = new();
        private Material _chopperWire;
        private bool _started;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Configure(PresentationBridge value, HomeFlyover city, Material wire, Material tracer)
        {
            bridge = value;
            flyover = city;
            wireMaterial = wire;
            tracerMaterial = tracer;
        }

        private void LateUpdate()
        {
            if (!_started)
            {
                if (flyover == null || !flyover.Ready) return;
                _started = true;
                Build();
            }
            var t = Time.unscaledTime;
            var dt = Time.unscaledDeltaTime;
            foreach (var flyer in _flyers) Fly(flyer, t, dt);
            foreach (var flyer in _flyers) Shoot(flyer, t);
            TickTracers(dt);
        }

        private void OnDestroy()
        {
            if (_chopperWire != null) Destroy(_chopperWire);
        }

        /// <summary>Choppers flying (for the camera's shots).</summary>
        public int FlyerCount => _flyers.Count;

        /// <summary>A chopper's position and velocity; false for a bad index.</summary>
        public bool TryGetFlyer(int index, out Vector3 position, out Vector3 velocity)
        {
            position = velocity = default;
            if (index < 0 || index >= _flyers.Count || _flyers[index].Root == null) return false;
            position = _flyers[index].Root.position;
            velocity = _flyers[index].Velocity;
            return true;
        }

        // ---- Setup ----

        private void Build()
        {
            var catalog = bridge != null && bridge.Config != null ? bridge.Config.LoadoutCatalog : null;
            if (catalog == null || catalog.Chassis.Count == 0 || count <= 0) return;
            if (wireMaterial != null)
            {
                _chopperWire = new Material(wireMaterial) { name = "Home Chopper Wire" };
                _chopperWire.SetFloat("_GridSize", 0.6f);
                _chopperWire.SetFloat("_MajorEvery", 3f);
                _chopperWire.SetColor("_LineColour", new Color(0.35f, 1.8f, 1f));
                _chopperWire.SetFloat("_RimStrength", 0.7f);
                _chopperWire.SetFloat("_WindowChance", 0f);
                _chopperWire.SetFloat("_ScanStrength", 0f);
            }

            for (var i = 0; i < count; i++)
            {
                var chassis = catalog.Chassis[i % catalog.Chassis.Count];
                var root = BuildChopper(chassis);
                if (root == null) continue;
                _flyers.Add(new Flyer
                {
                    Root = root,
                    Phase = i * 2.39996f,
                    // Spread round the camera ring: some inside it, some just outside.
                    Radius = flyover.Radius * Random.Range(0.45f, 1.05f),
                    Height = Random.Range(260f, 500f),
                    Rate = speed / Mathf.Max(flyover.Radius, 1f) * Random.Range(0.8f, 1.2f) * (i % 2 == 0 ? 1f : -1f),
                    NextBurst = Time.unscaledTime + Random.Range(0.5f, 3f),
                });
                CollectSpinners(_flyers[^1], chassis);
            }

            for (var i = 0; i < TracerPool; i++) _tracers.Add(MakeTracerEntry(i));
        }

        /// <summary>The chassis model with its default modules fitted, no colliders or scripts, in the chopper wire.</summary>
        private Transform BuildChopper(ChassisDefinition chassis)
        {
            var model = chassis != null && chassis.Prefab != null ? chassis.Prefab.transform.Find(HangarStage.ModelName) : null;
            if (model == null) return null;
            var root = new GameObject($"Menu Chopper ({chassis.DisplayName})").transform;
            root.SetParent(transform.parent, false);
            var body = Instantiate(model.gameObject, root, false).transform;
            body.localPosition = Vector3.zero;
            body.localRotation = Quaternion.identity;
            root.localScale = Vector3.one * scale;

            // Modules on the sockets, by slot order (as the hangar fits them).
            var sockets = body.GetComponentsInChildren<SocketAuthoring>(true);
            var ordinals = new Dictionary<SlotType, int>();
            var firstOfType = new Dictionary<SlotType, ModuleDefinition>();
            var used = new HashSet<SocketAuthoring>();
            var modules = chassis.DefaultModules;
            for (var slot = 0; slot < chassis.Slots.Count && slot < modules.Count; slot++)
            {
                var type = chassis.Slots[slot];
                ordinals.TryGetValue(type, out var ordinal);
                ordinals[type] = ordinal + 1;
                firstOfType.TryAdd(type, modules[slot]);
                var socket = FindSocket(sockets, type, ordinal);
                if (socket == null || modules[slot] == null || modules[slot].Visual == null) continue;
                used.Add(socket);
                Fit(modules[slot], socket);
            }
            foreach (var socket in sockets)
                if (!used.Contains(socket) && firstOfType.TryGetValue(socket.Slot, out var module) && module != null && module.Visual != null)
                    Fit(module, socket);

            foreach (var collider in root.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(script);
            if (_chopperWire != null)
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = r.sharedMaterials;
                    for (var m = 0; m < materials.Length; m++) materials[m] = _chopperWire;
                    r.sharedMaterials = materials;
                }
            return root;
        }

        private static SocketAuthoring FindSocket(SocketAuthoring[] sockets, SlotType type, int index)
        {
            foreach (var socket in sockets)
                if (socket.Slot == type && socket.Index == index) return socket;
            return null;
        }

        private static void Fit(ModuleDefinition module, SocketAuthoring socket)
        {
            var visual = Instantiate(module.Visual, socket.transform, false);
            visual.name = module.Visual.name;
            // Marks rotors for CollectSpinners (spin per socket: odd sockets turn the other way).
            if (module.VisualSpin != Vector3.zero) visual.name = "Spin:" + visual.name;
        }

        private static void CollectSpinners(Flyer flyer, ChassisDefinition chassis)
        {
            foreach (var part in flyer.Root.GetComponentsInChildren<Transform>(true))
            {
                if (!part.name.StartsWith("Spin:")) continue;
                var module = FindModule(chassis, part.name.Substring(5));
                if (module == null) continue;
                var socket = part.parent;
                var odd = socket != null && socket.name.EndsWith("1");
                flyer.Spinners.Add((part, module.VisualSpin * (odd ? -1f : 1f)));
            }
        }

        private static ModuleDefinition FindModule(ChassisDefinition chassis, string visualName)
        {
            foreach (var module in chassis.DefaultModules)
                if (module != null && module.Visual != null && module.Visual.name == visualName) return module;
            return null;
        }

        private LineRenderer MakeTracer(int index)
        {
            var go = new GameObject($"Menu Tracer {index}");
            go.transform.SetParent(transform.parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = tracerMaterial;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = tracerWidth;
            line.endWidth = tracerWidth * 0.3f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        // ---- Flight and fire ----

        /// <summary>A figure-eight round the city centre, with height drift; faces along its velocity and banks.</summary>
        private Vector3 PathPoint(Flyer flyer, float t)
        {
            var a = flyer.Phase + t * flyer.Rate;
            var centre = flyover.Centre;
            return centre + new Vector3(Mathf.Cos(a) * flyer.Radius, flyer.Height + Mathf.Sin(a * 3f + flyer.Phase) * 30f,
                Mathf.Sin(2f * a) * flyer.Radius * 0.55f);
        }

        private void Fly(Flyer flyer, float t, float dt)
        {
            var position = PathPoint(flyer, t);
            var ahead = PathPoint(flyer, t + 0.4f);
            var further = PathPoint(flyer, t + 0.8f);
            var velocity = (ahead - position) / 0.4f;
            var nextVelocity = (further - ahead) / 0.4f;
            flyer.Velocity = velocity;
            var forward = velocity.sqrMagnitude > 0.01f ? velocity.normalized : flyer.Root.forward;
            // Bank into the turn (sign of the yaw change), and pitch the nose down a little as choppers do.
            var turn = Vector3.SignedAngle(new Vector3(velocity.x, 0f, velocity.z), new Vector3(nextVelocity.x, 0f, nextVelocity.z), Vector3.up);
            var bank = Mathf.Clamp(-turn * 1.5f, -45f, 45f);
            var rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(8f, 0f, bank);
            flyer.Root.SetPositionAndRotation(position, Quaternion.Slerp(flyer.Root.rotation, rotation, 1f - Mathf.Exp(-6f * dt)));
            foreach (var (part, spin) in flyer.Spinners) part.Rotate(spin * dt, Space.Self);
        }

        private void Shoot(Flyer flyer, float t)
        {
            if (flyer.ShotsLeft <= 0)
            {
                if (t < flyer.NextBurst) return;
                flyer.Target = Nearest(flyer);
                if (flyer.Target == null) return;
                flyer.ShotsLeft = Random.Range(5, 10);
                flyer.NextShot = t;
                flyer.NextBurst = t + Random.Range(1.5f, 4f);
            }
            if (t < flyer.NextShot) return;
            flyer.NextShot = t + 0.07f;
            flyer.ShotsLeft--;

            var muzzle = flyer.Root.position + flyer.Root.forward * 4f * scale;
            // Lead the target a little, with a spread so most rounds miss close by.
            var aim = flyer.Target.Root.position + flyer.Target.Velocity * 0.25f + Random.insideUnitSphere * 18f;
            Fire(muzzle, (aim - muzzle).normalized, (aim - muzzle).magnitude);
        }

        private Flyer Nearest(Flyer from)
        {
            Flyer best = null;
            var bestDistance = float.MaxValue;
            foreach (var other in _flyers)
            {
                if (other == from) continue;
                var d = (other.Root.position - from.Root.position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = other;
                }
            }
            return best;
        }

        private void Fire(Vector3 start, Vector3 direction, float range)
        {
            foreach (var tracer in _tracers)
            {
                if (tracer.Line.enabled) continue;
                tracer.Start = start;
                tracer.Direction = direction;
                tracer.Age = 0f;
                tracer.Life = Mathf.Max(range * 1.3f, 200f) / Mathf.Max(tracerSpeed, 1f);
                tracer.Line.enabled = true;
                return;
            }
        }

        private void TickTracers(float dt)
        {
            foreach (var tracer in _tracers)
            {
                if (!tracer.Line.enabled) continue;
                tracer.Age += dt;
                if (tracer.Age >= tracer.Life)
                {
                    tracer.Line.enabled = false;
                    continue;
                }
                var head = tracer.Start + tracer.Direction * (tracer.Age * tracerSpeed);
                var tailDistance = Mathf.Max(0f, tracer.Age * tracerSpeed - tracerLength);
                tracer.Line.SetPosition(0, tracer.Start + tracer.Direction * tailDistance);
                tracer.Line.SetPosition(1, head);
            }
        }

        private Tracer MakeTracerEntry(int index) => new() { Line = MakeTracer(index) };
    }
}
