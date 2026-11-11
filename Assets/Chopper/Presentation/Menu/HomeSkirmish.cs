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
        private float scale = 3f;
        [Header("Combat flight")]
        [SerializeField, Tooltip("Top speed, metres per second (fast attack choppers: about 75).")]
        private float maxSpeed = 75f;
        [SerializeField, Tooltip("Most acceleration the rotor gives, m/s² (sets how hard they turn and stop).")]
        private float maxAcceleration = 14f;
        [SerializeField, Tooltip("Flight band above the city ground, metres.")]
        private Vector2 altitude = new(140f, 460f);
        [SerializeField, Tooltip("Arena radius as a share of the camera orbit radius.")]
        private float arenaScale = 1.1f;
        [SerializeField, Tooltip("Seconds of chase before a break, min and max.")]
        private Vector2 chaseSeconds = new(5f, 10f);
        [SerializeField, Tooltip("Seconds of a break (hard jink away), min and max.")]
        private Vector2 breakSeconds = new(1.5f, 3f);
        [SerializeField, Tooltip("Fire when the target is closer than this and within the cone.")]
        private float fireRange = 600f;
        [SerializeField, Tooltip("Half-angle of the gun cone, degrees.")]
        private float fireCone = 14f;
        [SerializeField, Tooltip("Fastest turn, degrees per second (sets the tightest circle at speed).")]
        private float maxTurnRate = 42f;
        [SerializeField, Tooltip("How fast the turn rate builds and fades, degrees per second² (roll in / roll out).")]
        private float turnAcceleration = 30f;
        [SerializeField, Tooltip("Turn rate per degree of heading error (lower = lazier, never springy).")]
        private float turnGain = 0.9f;
        [SerializeField, Tooltip("Steepest climb or dive, degrees.")]
        private float maxClimbAngle = 25f;
        [SerializeField, Tooltip("How fast the climb angle changes, degrees per second.")]
        private float climbRate = 18f;
        [SerializeField, Tooltip("How fast speed changes, m/s².")]
        private float speedChange = 9f;
        [SerializeField, Range(0.5f, 10f), Tooltip("Smoothing of the felt thrust (bank and pitch), per second.")]
        private float thrustEase = 4f;
        [SerializeField, Range(0.5f, 10f), Tooltip("How fast the body follows its attitude (per second).")]
        private float bodyEase = 3.5f;
        [SerializeField] private float tracerSpeed = 900f;
        [SerializeField] private float tracerLength = 55f;
        [SerializeField] private float tracerWidth = 1.6f;

        private enum Mode { Chase, Break }

        private sealed class Flyer
        {
            public Transform Root;
            public readonly List<(Transform Part, Vector3 Spin)> Spinners = new();
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Acceleration;
            /// <summary>Heading, degrees (0 = +Z); turn rate, degrees per second; climb angle, degrees; speed, m/s.</summary>
            public float Yaw;
            public float YawRate;
            public float Climb;
            public float Speed;
            /// <summary>0 = nose along the flight path, 1 = nose on the target; eased.</summary>
            public float AimBlend;
            public Flyer Target;
            public Mode Mode;
            public float ModeEnd;
            public Vector3 BreakDirection;
            public float NextShot;
            public int ShotsLeft;
            public float NextBurst;
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
                // A clean silhouette: dark body, bright outline (rim), a few panel lines; no dense grid (it turns the
                // small, moving shapes into noise at the menu's pixel size).
                _chopperWire.SetFloat("_GridSize", 6f);
                _chopperWire.SetFloat("_MajorEvery", 1f);
                _chopperWire.SetFloat("_MinorStrength", 0f);
                _chopperWire.SetFloat("_LineWidth", 1.2f);
                _chopperWire.SetColor("_LineColour", new Color(0.3f, 1.5f, 0.85f));
                _chopperWire.SetColor("_FillColour", new Color(0.01f, 0.09f, 0.05f, 1f));
                _chopperWire.SetFloat("_RimPower", 2.2f);
                _chopperWire.SetFloat("_RimStrength", 1.6f);
                _chopperWire.SetFloat("_WindowChance", 0f);
                _chopperWire.SetFloat("_ScanStrength", 0f);
            }

            for (var i = 0; i < count; i++)
            {
                var chassis = catalog.Chassis[i % catalog.Chassis.Count];
                var root = BuildChopper(chassis);
                if (root == null) continue;
                // Start spread round the arena, heading round it, at cruise speed.
                var angle = i * Mathf.PI * 2f / count;
                var start = flyover.Centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Arena * 0.7f +
                            Vector3.up * Random.Range(altitude.x, altitude.y);
                var heading = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                _flyers.Add(new Flyer
                {
                    Root = root,
                    Position = start,
                    Velocity = heading * maxSpeed * 0.7f,
                    Yaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg,
                    Speed = maxSpeed * 0.7f,
                    Mode = Mode.Chase,
                    ModeEnd = Time.unscaledTime + Random.Range(chaseSeconds.x, chaseSeconds.y),
                });
                root.SetPositionAndRotation(start, Quaternion.LookRotation(heading));
                CollectSpinners(_flyers[^1], chassis);
            }
            // Duels: each chopper hunts the next one round (a chase ring keeps everyone busy).
            for (var i = 0; i < _flyers.Count; i++) _flyers[i].Target = _flyers.Count > 1 ? _flyers[(i + 1) % _flyers.Count] : null;

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

        private float Arena => flyover.Radius * arenaScale;

        /// <summary>
        /// Steering: a wanted velocity (chase a point behind the target, or break away hard), with arena, altitude and
        /// tower avoidance; then bank-to-turn flight toward it (limited, eased turn rate; limited climb and speed change).
        /// Attitude from the thrust: the rotor points along acceleration + gravity, so turns bank, speeding up dips the
        /// nose and braking flares it.
        /// </summary>
        private void Fly(Flyer flyer, float t, float dt)
        {
            if (dt <= 0f) return;
            var centre = flyover.Centre;
            var target = flyer.Target;

            // Mode: chase for a while, then break (a hard jink to one side and up or down), then chase again.
            if (t >= flyer.ModeEnd)
            {
                if (flyer.Mode == Mode.Chase)
                {
                    flyer.Mode = Mode.Break;
                    flyer.ModeEnd = t + Random.Range(breakSeconds.x, breakSeconds.y);
                    var side = Vector3.Cross(Vector3.up, flyer.Velocity.sqrMagnitude > 1f ? flyer.Velocity.normalized : flyer.Root.forward).normalized;
                    flyer.BreakDirection = (side * (Random.value < 0.5f ? 1f : -1f) + Vector3.up * Random.Range(-0.5f, 0.6f)).normalized;
                }
                else
                {
                    flyer.Mode = Mode.Chase;
                    flyer.ModeEnd = t + Random.Range(chaseSeconds.x, chaseSeconds.y);
                }
            }
            // Being shot at from close behind: break early.
            if (flyer.Mode == Mode.Chase && IsChasedClosely(flyer) && Random.value < dt * 0.6f) flyer.ModeEnd = t;

            Vector3 wanted;
            if (flyer.Mode == Mode.Break || target == null)
                wanted = (flyer.Velocity.normalized * 0.4f + flyer.BreakDirection).normalized * maxSpeed;
            else
            {
                // Pursuit: lead the target and sit a little behind and above it.
                var toTarget = target.Position - flyer.Position;
                var lead = Mathf.Clamp(toTarget.magnitude / maxSpeed, 0f, 2.5f);
                var behind = target.Velocity.sqrMagnitude > 1f ? target.Velocity.normalized : Vector3.forward;
                var aimPoint = target.Position + target.Velocity * lead - behind * 90f + Vector3.up * 15f;
                var toAim = aimPoint - flyer.Position;
                // Ease off when close, so they do not ram (but stay fast).
                var speed = Mathf.Lerp(maxSpeed * 0.75f, maxSpeed, Mathf.InverseLerp(80f, 400f, toAim.magnitude));
                wanted = toAim.normalized * speed;
            }

            // Arena: past its edge, turn back towards the centre.
            var flat = new Vector3(flyer.Position.x - centre.x, 0f, flyer.Position.z - centre.z);
            var over = flat.magnitude - Arena;
            if (over > 0f) wanted = Vector3.Lerp(wanted, -flat.normalized * maxSpeed, Mathf.Clamp01(over / 300f));

            // Altitude band, and climb over towers ahead (they have no colliders).
            var height = flyer.Position.y - centre.y;
            if (height < altitude.x) wanted.y += (altitude.x - height) * 0.8f;
            if (height > altitude.y) wanted.y -= (height - altitude.y) * 0.8f;
            var look = flyer.Position + flyer.Velocity * 4f;
            foreach (var b in flyover.Buildings)
            {
                if (look.x < b.min.x - 40f || look.x > b.max.x + 40f || look.z < b.min.z - 40f || look.z > b.max.z + 40f) continue;
                if (look.y < b.max.y + 40f) wanted.y = Mathf.Max(wanted.y, (b.max.y + 60f - flyer.Position.y) * 1.2f);
            }

            // Bank-to-turn flight, no springs (they overshoot and swing back): the heading turns at a rate that is itself
            // limited and eased (roll in, roll out); climb angle and speed change at limited rates. A chopper that passes
            // its target simply keeps turning in a wide arc.
            var oldVelocity = flyer.Velocity;
            var flatWanted = new Vector2(wanted.x, wanted.z);
            if (flatWanted.sqrMagnitude > 0.01f)
            {
                var wantedYaw = Mathf.Atan2(flatWanted.x, flatWanted.y) * Mathf.Rad2Deg;
                var error = Mathf.DeltaAngle(flyer.Yaw, wantedYaw);
                var targetRate = Mathf.Clamp(error * turnGain, -maxTurnRate, maxTurnRate);
                flyer.YawRate = Mathf.MoveTowards(flyer.YawRate, targetRate, turnAcceleration * dt);
            }
            flyer.Yaw += flyer.YawRate * dt;
            var wantedClimb = Mathf.Clamp(Mathf.Atan2(wanted.y, Mathf.Max(flatWanted.magnitude, 1f)) * Mathf.Rad2Deg, -maxClimbAngle, maxClimbAngle);
            flyer.Climb = Mathf.MoveTowards(flyer.Climb, wantedClimb, climbRate * dt);
            flyer.Speed = Mathf.MoveTowards(flyer.Speed, Mathf.Min(wanted.magnitude, maxSpeed), speedChange * dt);
            var direction = Quaternion.Euler(-flyer.Climb, flyer.Yaw, 0f) * Vector3.forward;
            flyer.Velocity = direction * flyer.Speed;
            flyer.Position += flyer.Velocity * dt;

            // Attitude: rotor up = thrust (a + g), from the real change in velocity (smoothed a little: no jitter), so the
            // bank follows the turn rate and the nose dips or flares with speed changes.
            var acceleration = (flyer.Velocity - oldVelocity) / dt;
            flyer.Acceleration = Vector3.Lerp(flyer.Acceleration, Vector3.ClampMagnitude(acceleration, maxAcceleration), 1f - Mathf.Exp(-thrustEase * dt));
            var up = (flyer.Acceleration + Vector3.up * 9.81f).normalized;
            var heading = direction;
            // In the gun cone the nose swings a little onto the target (pedal turn), eased in and out.
            var aiming = target != null && flyer.Mode == Mode.Chase && InGunCone(flyer, target, fireCone * 2f);
            flyer.AimBlend = Mathf.MoveTowards(flyer.AimBlend, aiming ? 1f : 0f, dt * 0.8f);
            if (target != null && flyer.AimBlend > 0f)
                heading = Vector3.Slerp(heading, (target.Position - flyer.Position).normalized, Mathf.SmoothStep(0f, 0.35f, flyer.AimBlend));
            var forward = Vector3.ProjectOnPlane(heading, up);
            if (forward.sqrMagnitude < 0.001f) forward = flyer.Root.forward;
            var rotation = Quaternion.LookRotation(forward.normalized, up);
            flyer.Root.SetPositionAndRotation(flyer.Position, Quaternion.Slerp(flyer.Root.rotation, rotation, 1f - Mathf.Exp(-bodyEase * dt)));
            foreach (var (part, spin) in flyer.Spinners) part.Rotate(spin * dt, Space.Self);
        }

        private bool IsChasedClosely(Flyer flyer)
        {
            foreach (var other in _flyers)
                if (other.Target == flyer && other.Mode == Mode.Chase && (other.Position - flyer.Position).sqrMagnitude < 250f * 250f &&
                    InGunCone(other, flyer, fireCone * 1.5f))
                    return true;
            return false;
        }

        private bool InGunCone(Flyer shooter, Flyer target, float cone)
        {
            var to = target.Position - shooter.Position;
            if (to.sqrMagnitude > fireRange * fireRange) return false;
            return Vector3.Angle(shooter.Root.forward, to) <= cone;
        }

        /// <summary>Bursts only with the nose on the target, in range; tracers along the nose with a little spread.</summary>
        private void Shoot(Flyer flyer, float t)
        {
            var target = flyer.Target;
            if (target == null || flyer.Mode != Mode.Chase) return;
            if (flyer.ShotsLeft <= 0)
            {
                if (t < flyer.NextBurst || !InGunCone(flyer, target, fireCone)) return;
                flyer.ShotsLeft = Random.Range(6, 14);
                flyer.NextShot = t;
                flyer.NextBurst = t + Random.Range(0.6f, 1.6f);
            }
            if (t < flyer.NextShot) return;
            flyer.NextShot = t + 0.06f;
            flyer.ShotsLeft--;

            var muzzle = flyer.Position + flyer.Root.forward * 4f * scale - flyer.Root.up * 1.5f * scale;
            var toTarget = target.Position + target.Velocity * ((target.Position - muzzle).magnitude / tracerSpeed) - muzzle;
            var direction = Vector3.Slerp(flyer.Root.forward, toTarget.normalized, 0.6f);
            direction = Quaternion.Euler(Random.Range(-1.2f, 1.2f), Random.Range(-1.2f, 1.2f), 0f) * direction;
            Fire(muzzle, direction.normalized, toTarget.magnitude);
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
