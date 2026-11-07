using System;
using System.Collections.Generic;
using System.Threading;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Flow;
using Chopper.Services.Matchmaking;
using Cysharp.Threading.Tasks;
using Chopper.Simulation;
using Chopper.Simulation.Loadout;
using Chopper.Simulation.Session;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chopper.Presentation.Hangar
{
    /// <summary>
    /// The hangar's 3D view (plan section 5): the chosen chassis on a turntable, built from the same model, sockets and
    /// module prefabs as the game ("what you see is what flies"). The real paint with the chosen skin, and a subtle green
    /// wireframe over it (lines baked in the <see cref="WireframeLibrary"/>); the selected slot's module lines in the
    /// highlight colour; empty sockets pulse. Gives the UI each slot's screen point (callouts). The camera and turntable blend to the
    /// selected socket's focus pose (<see cref="SocketAuthoring"/>); the overview turns slowly. The player turns the
    /// chopper by mouse drag or right stick (<see cref="Drag"/>, <see cref="Steer"/>). A new chassis or module is built by a scan line from
    /// bottom to top (each part shows with bright lines as the line reaches it); a fitted module also slides onto its
    /// socket. Hangar sounds: fit, remove, slot focus, chassis scan. Launch starts a local
    /// session (<see cref="LocalSession"/>) and loads the match scene. Set up by Chopper → Setup, step 7.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HangarStage : MonoBehaviour
    {
        /// <summary>The hangar scene (Setup step 7 makes it and puts it in the build settings).</summary>
        public const string SceneName = "Hangar";
        /// <summary>Child of a chassis prefab that holds its model and sockets (Setup step 6).</summary>
        public const string ModelName = "Model";
        private const string WireName = "Wire";
        /// <summary>Exponential blend: e^-3 = 5 % left after the blend time.</summary>
        private const float BlendRate = 3f;
        /// <summary>A socket this far off the centre line (metres, or this part of the chopper's size) is on a side.</summary>
        private const float LateralMinimum = 0.2f;
        private const float LateralFraction = 0.04f;
        /// <summary>How much of the front shows with a side view: 0 = straight on.</summary>
        private const float FrontBias = 0.35f;
        /// <summary>How much of the side shows with a front or back view (nose, tail): 1 = a 3/4 view.</summary>
        private const float SideBias = 0.8f;
        /// <summary>A module this high (part of the chopper's half height above the centre) is on top: rotor.</summary>
        private const float TopFraction = 0.5f;

        [SerializeField, Tooltip("Scene presentation bridge; it owns the hangar model.")] private PresentationBridge bridge;
        [SerializeField] private UnityEngine.Camera viewCamera;
        [SerializeField, Tooltip("The chopper turns on this.")] private Transform turntable;
        [SerializeField, Tooltip("Scene loaded by launch when there is no map catalog, and for an online server that does not " +
                                  "announce its map.")] private string matchScene = ChopperBootstrap.TrainingRangeScene;

        private sealed class Part
        {
            public Renderer Renderer;
            public MeshRenderer Wire;
            /// <summary>Loadout slot of the module (-1 = the chassis).</summary>
            public int Slot;
            /// <summary>The renderer's own state (some model parts start off).</summary>
            public bool Shown;
            /// <summary>Unscaled time the scan reaches this part (hidden before it); 0 = shown.</summary>
            public float RevealAt;
            /// <summary>Unscaled time its scan glow ends.</summary>
            public float GlowUntil;
        }

        private readonly List<Part> _parts = new();
        private readonly List<Spinner> _spinners = new();

        /// <summary>
        /// A turning module visual (rotor). It holds still while it builds and while its slot is open (so the player
        /// can see the blades), then spins up again.
        /// </summary>
        private sealed class Spinner
        {
            public Transform Visual;
            public Quaternion Rest;
            /// <summary>Degrees per second on each axis.</summary>
            public Vector3 Spin;
            public int Slot;
            /// <summary>Seconds of full-speed spin so far (the angle is Spin times this).</summary>
            public float Phase;
            /// <summary>Unscaled time it may start to spin up.</summary>
            public float HoldUntil;
            /// <summary>0 = still, 1 = full speed; ramps both ways.</summary>
            public float Speed = 1f;
        }
        /// <summary>Socket of each loadout slot (null when the model has none).</summary>
        private readonly List<SocketAuthoring> _slotSockets = new();
        private readonly List<(GameObject Marker, int Slot)> _markers = new();
        /// <summary>Fitted modules sliding onto their sockets.</summary>
        private readonly List<(Transform Visual, Vector3 Home, Vector3 From, float Start)> _slides = new();
        /// <summary>Slots whose module changed in this build (they scan and slide in).</summary>
        private readonly HashSet<int> _changedSlots = new();
        private readonly Dictionary<SlotType, int> _ordinals = new();
        private PresentationContext _context;
        private HangarModel _model;
        private HangarSettings _settings;
        private GameObject _chopper;
        private ChassisDefinition _builtChassis;
        private ModuleDefinition[] _builtModules = System.Array.Empty<ModuleDefinition>();
        private float _size = 10f;
        /// <summary>Half the chopper's height (metres), measured on the turntable.</summary>
        private float _halfHeight = 1f;
        /// <summary>
        /// Per slot, what the camera frames when the slot is open: the module's bounds (else the socket and the marker
        /// size), in turntable space at yaw 0.
        /// </summary>
        private readonly List<(Vector3 Centre, float Radius)> _slotTargets = new();
        /// <summary>Per slot: the turntable yaw that shows its module with no other module in the way.</summary>
        private readonly List<float> _slotYaws = new();
        /// <summary>Per slot: degrees the camera looks down at its module (below 0 = from below).</summary>
        private readonly List<float> _slotElevations = new();
        /// <summary>When no side shows a module under the chopper, look from this far below (degrees).</summary>
        private const float SteepBelow = -40f;
        /// <summary>Per slot: its module's bounds in turntable space (null when empty); used to find a clear view.</summary>
        private readonly List<Bounds?> _slotBounds = new();
        /// <summary>Mesh colliders of the chopper's parts and their slots (-1 = body), only while <see cref="MeasureSlots"/> runs.</summary>
        private readonly Dictionary<MeshCollider, int> _bodyColliders = new();
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        /// <summary>Reused: points on a module the camera should see.</summary>
        private readonly List<Vector3> _lookPoints = new();
        /// <summary>Reused: views tried for a slot, best first.</summary>
        private readonly List<Vector2> _sides = new();
        /// <summary>Armour covers the body: frame this part of the chopper's length at least.</summary>
        private const float ArmourFraction = 0.3f;
        /// <summary>Metres the camera stays under the rotor disc when it looks at a module below it.</summary>
        private const float RotorClearance = 0.25f;
        /// <summary>A middle module this far behind the centre (metres) is seen from behind.</summary>
        private const float BackOffset = 0.5f;
        /// <summary>Lowest point of the rotor modules (turntable space); null when no rotor is fitted.</summary>
        private float? _rotorBottom;
        private float _yaw = 150f;
        /// <summary>Idle spin now (degrees per second); eases between the setting and 0.</summary>
        private float _spin;
        private MaterialPropertyBlock _block;
        private Mesh _markerMesh;
        private bool _launching;
        private IMatchmakingService _matchmaking;
        private CancellationTokenSource _search;
        /// <summary>Degrees of turn asked for since the last frame (drag, stick).</summary>
        private float _turnInput;
        private bool _turnFlings;
        /// <summary>Fling speed after a drag (degrees per second); it slows down.</summary>
        private float _turnVelocity;
        /// <summary>Slot focus: the player's turn away from the socket's view (degrees).</summary>
        private float _focusTurn;
        /// <summary>Seconds left before the idle spin starts again after the player turned the chopper.</summary>
        private float _spinWait;
        private int _lastFocus = HangarModel.Overview;
        /// <summary>Turntable yaw when a top slot (rotor) opened: the chopper does not turn for it.</summary>
        private float _topYaw;
        /// <summary>Unscaled time the last scan or slide ends: parts get their look each frame until then.</summary>
        private float _animationEnd;
        private bool _built;
        /// <summary>This build changed modules on the same chassis: the new ones slide in.</summary>
        private bool _slideIn;

        /// <summary>
        /// True while the pointer holds the chopper (a drag): no idle spin and no fling until it lets go.
        /// </summary>
        public bool Holding { get; set; }

        /// <summary>
        /// Turns the chopper by <paramref name="degrees"/> this frame (<see cref="Drag"/>, <see cref="Steer"/>). In the overview it turns the turntable; with a
        /// slot open it turns away from the socket's view, up to <see cref="HangarSettings.FocusTurnLimit"/>.
        /// <paramref name="fling"/>: keep turning after the pointer lets go (mouse drag, not the stick).
        /// </summary>
        private void Turn(float degrees, bool fling)
        {
            _turnInput += degrees;
            _turnFlings = fling;
        }

        /// <summary>Mouse drag across the view by <paramref name="pixels"/> this frame (right = positive).</summary>
        public void Drag(float pixels)
        {
            // Positive yaw moves the near side to the left: a drag to the right turns the other way.
            if (_settings != null) Turn(-pixels * _settings.DragDegreesPerPixel, fling: true);
        }

        /// <summary>Gamepad stick, -1..1 (right = positive), held this frame.</summary>
        public void Steer(float axis)
        {
            if (_settings != null) Turn(-axis * _settings.StickTurnSpeed * Time.unscaledDeltaTime, fling: false);
        }

        /// <summary>Online play: set by the hangar scene's lifetime scope (the root scope's matchmaking service).</summary>
        public void UseMatchmaking(IMatchmakingService matchmaking) => _matchmaking = matchmaking;

        /// <summary>
        /// Where slot <paramref name="slot"/>'s socket is on screen, in viewport space (0..1, y up). False when the slot
        /// has no socket or the socket is behind the camera.
        /// </summary>
        public bool TryGetSlotViewportPoint(int slot, out Vector2 point)
        {
            point = default;
            if (slot < 0 || slot >= _slotSockets.Count || _slotSockets[slot] == null || viewCamera == null) return false;
            var viewport = viewCamera.WorldToViewportPoint(_slotSockets[slot].transform.position);
            point = new Vector2(viewport.x, viewport.y);
            return viewport.z > 0f;
        }

        /// <summary>The chopper's centre in viewport space (callout cards sit outward from it).</summary>
        public Vector2 CentreViewportPoint
        {
            get
            {
                if (viewCamera == null || turntable == null) return new Vector2(0.5f, 0.5f);
                var viewport = viewCamera.WorldToViewportPoint(turntable.position);
                return new Vector2(viewport.x, viewport.y);
            }
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Configure(PresentationBridge presentation, UnityEngine.Camera view, Transform table)
        {
            bridge = presentation;
            viewCamera = view;
            turntable = table;
        }

        private void OnEnable()
        {
            if (bridge == null || viewCamera == null || turntable == null)
            {
                Debug.LogError($"{name}: bridge, camera or turntable not set. Run Chopper → Setup, step 7.", this);
                enabled = false;
                return;
            }
            _context = bridge.Context;
            _model = _context.Hangar;
            _settings = _context.Config != null ? _context.Config.Hangar : null;
            if (_model == null || _settings == null)
            {
                Debug.LogError($"{name}: the game config has no loadout catalog or hangar settings. Run Chopper → Setup.", this);
                enabled = false;
                return;
            }
            _model.Changed += OnChanged;
            _model.Launched += OnLaunched;
            _model.MatchRequested += OnMatchRequested;
            _model.SearchCancelled += OnSearchCancelled;
            OnChanged();
            SnapCamera();
        }

        private void OnDisable()
        {
            if (_model == null) return;
            _model.Changed -= OnChanged;
            _model.Launched -= OnLaunched;
            _model.MatchRequested -= OnMatchRequested;
            _model.SearchCancelled -= OnSearchCancelled;
            OnSearchCancelled();
        }

        private void OnDestroy()
        {
            if (_markerMesh != null) Destroy(_markerMesh);
        }

        private void OnChanged()
        {
            if (_model.Chassis != _builtChassis || !SameModules(_model.Fitted)) Build();
            if (_model.FocusedSlot != _lastFocus)
            {
                _lastFocus = _model.FocusedSlot;
                _topYaw = _yaw;
                _focusTurn = 0f;
                _turnVelocity = 0f;
                Play(_context.Sounds != null ? _context.Sounds.SlotFocus : null);
            }
            ApplyLook();
        }

        private void Play(Audio.SoundCue cue)
        {
            if (cue != null) _context.Audio.Play(cue);
        }

        private bool SameModules(IReadOnlyList<ModuleDefinition> fitted)
        {
            if (fitted.Count != _builtModules.Length) return false;
            for (var i = 0; i < fitted.Count; i++)
                if (fitted[i] != _builtModules[i]) return false;
            return true;
        }

        private void Build()
        {
            if (_chopper != null) Destroy(_chopper);
            _parts.Clear();
            _spinners.Clear();
            _slotSockets.Clear();
            _markers.Clear();
            _slides.Clear();

            var chassis = _model.Chassis;
            var fitted = _model.Fitted;
            var newChassis = chassis != _builtChassis;
            var fittedSome = false;
            var removedSome = false;
            _changedSlots.Clear();
            for (var i = 0; i < fitted.Count; i++)
            {
                var before = !newChassis && i < _builtModules.Length ? _builtModules[i] : null;
                if (fitted[i] == before) continue;
                if (fitted[i] != null) _changedSlots.Add(i);
                if (newChassis) continue;
                fittedSome |= fitted[i] != null;
                removedSome |= fitted[i] == null;
            }
            // The first build (scene start) scans in too, but without a sound.
            var sounds = _context.Sounds;
            if (_built && sounds != null)
            {
                if (newChassis) Play(sounds.ChassisScan);
                else if (fittedSome) Play(sounds.ModuleFit);
                else if (removedSome) Play(sounds.ModuleRemove);
            }
            _built = true;
            _slideIn = !newChassis;
            _builtChassis = chassis;
            _builtModules = new ModuleDefinition[fitted.Count];
            for (var i = 0; i < fitted.Count; i++) _builtModules[i] = fitted[i];

            var source = chassis != null && chassis.Prefab != null ? chassis.Prefab.transform.Find(ModelName) : null;
            if (source == null)
            {
                if (chassis != null) Debug.LogWarning($"Hangar: {chassis.name}'s prefab has no '{ModelName}' child. Run Setup step 6.", chassis);
                return;
            }

            _chopper = Instantiate(source.gameObject, turntable, false);
            _chopper.name = chassis.DisplayName;
            _chopper.transform.localPosition = Vector3.zero;
            _chopper.transform.localRotation = Quaternion.identity;
            AddParts(_chopper, -1);
            FitModules(chassis, fitted);
            Centre();
            StartScan(newChassis);
        }

        /// <summary>
        /// New chassis: every part scans in. Else only the parts of the changed slots. The scan line goes up through
        /// the parts being built over <see cref="HangarSettings.ScanSeconds"/>.
        /// </summary>
        private void StartScan(bool everything)
        {
            var start = Time.unscaledTime;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var part in _parts)
            {
                if (!everything && !_changedSlots.Contains(part.Slot)) continue;
                min = Mathf.Min(min, part.Renderer.bounds.min.y);
                max = Mathf.Max(max, part.Renderer.bounds.min.y);
            }
            if (min > max) return;
            var height = Mathf.Max(0.01f, max - min);
            foreach (var part in _parts)
            {
                if (!everything && !_changedSlots.Contains(part.Slot)) continue;
                part.RevealAt = start + _settings.ScanSeconds * Mathf.Clamp01((part.Renderer.bounds.min.y - min) / height);
                part.GlowUntil = part.RevealAt + _settings.ScanGlowSeconds;
                _animationEnd = Mathf.Max(_animationEnd, part.GlowUntil);
            }
            _animationEnd = Mathf.Max(_animationEnd, start + _settings.FitSlideSeconds);
            // New rotors hold still until their scan and slide end.
            foreach (var spinner in _spinners)
                if (everything || _changedSlots.Contains(spinner.Slot))
                {
                    spinner.HoldUntil = _animationEnd;
                    spinner.Speed = 0f;
                }
            // After Centre (it measures the model at rest): parts hide, slides start out.
            ApplySlides(start);
        }

        /// <summary>Same mapping as the game (<c>ModuleVisualSystem</c>): slot order picks the socket of that type by
        /// index; a socket with no slot of its own repeats the first module of its type (tandem rotors).</summary>
        private void FitModules(ChassisDefinition chassis, IReadOnlyList<ModuleDefinition> fitted)
        {
            var sockets = _chopper.GetComponentsInChildren<SocketAuthoring>(true);
            var used = new HashSet<SocketAuthoring>();
            var firstOfType = new Dictionary<SlotType, int>();
            _ordinals.Clear();
            for (var slot = 0; slot < chassis.Slots.Count; slot++)
            {
                var type = chassis.Slots[slot];
                _ordinals.TryGetValue(type, out var ordinal);
                _ordinals[type] = ordinal + 1;
                firstOfType.TryAdd(type, slot);

                var socket = Find(sockets, type, ordinal);
                _slotSockets.Add(socket);
                if (socket == null) continue;
                used.Add(socket);
                var module = slot < fitted.Count ? fitted[slot] : null;
                if (module != null && module.Visual != null) AddModule(module, socket, slot);
                else AddMarker(socket, slot);
            }

            foreach (var socket in sockets)
            {
                if (used.Contains(socket) || !firstOfType.TryGetValue(socket.Slot, out var slot)) continue;
                var module = fitted[slot];
                if (module != null && module.Visual != null) AddModule(module, socket, slot);
            }
        }

        private static SocketAuthoring Find(SocketAuthoring[] sockets, SlotType type, int index)
        {
            foreach (var socket in sockets)
                if (socket.Slot == type && socket.Index == index) return socket;
            return null;
        }

        private void AddModule(ModuleDefinition module, SocketAuthoring socket, int slot)
        {
            var visual = Instantiate(module.Visual, socket.transform, false);
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            AddParts(visual, slot);
            if (_slideIn && _changedSlots.Contains(slot) && _settings.FitSlideSeconds > 0f)
            {
                // Slides in from outside the chopper (away from its axis, a little from above).
                var outward = socket.transform.position - turntable.position;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.zero;
                var from = (outward + Vector3.up * 0.35f).normalized * _settings.FitSlideDistance;
                var rest = visual.transform.localPosition;
                _slides.Add((visual.transform, rest, rest + socket.transform.InverseTransformVector(from), Time.unscaledTime));
            }
            if (module.VisualSpin != Vector3.zero)
                _spinners.Add(new Spinner
                {
                    Visual = visual.transform,
                    Rest = visual.transform.localRotation,
                    Spin = module.VisualSpin * (socket.Index % 2 == 0 ? 1f : -1f),
                    Slot = slot,
                });
        }

        /// <summary>Each mesh keeps its paint and gets its baked line mesh (the wireframe overlay).</summary>
        private void AddParts(GameObject root, int slot)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name == WireName || !filter.TryGetComponent<Renderer>(out var target)) continue;
                MeshRenderer wire = null;
                if (_settings.Wireframes != null && _settings.Wireframes.TryGet(filter.sharedMesh, out var lines))
                {
                    var go = new GameObject(WireName);
                    go.transform.SetParent(filter.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = lines;
                    wire = go.AddComponent<MeshRenderer>();
                    wire.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    wire.receiveShadows = false;
                }
                _parts.Add(new Part { Renderer = target, Wire = wire, Slot = slot, Shown = target.enabled });
            }
        }

        private void AddMarker(SocketAuthoring socket, int slot)
        {
            _markerMesh ??= MarkerMesh();
            var marker = new GameObject("Empty Socket");
            marker.transform.SetParent(socket.transform, false);
            marker.AddComponent<MeshFilter>().sharedMesh = _markerMesh;
            var renderer = marker.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _settings.EmptySocket;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _markers.Add((marker, slot));
        }

        /// <summary>Puts the model's centre on the turntable axis, and measures its size for the camera.</summary>
        private void Centre()
        {
            var bounds = new Bounds();
            var first = true;
            foreach (var part in _parts)
            {
                if (first) bounds = part.Renderer.bounds;
                else bounds.Encapsulate(part.Renderer.bounds);
                first = false;
            }
            if (first) return;
            var centre = turntable.InverseTransformPoint(bounds.center);
            _chopper.transform.localPosition -= centre;
            _size = Mathf.Max(1f, Mathf.Max(bounds.size.x, bounds.size.z));
            _halfHeight = Mathf.Max(0.5f, bounds.extents.y);
            MeasureSlots();
        }

        /// <summary>
        /// Each slot's focus target in turntable space (so it does not change as the turntable turns): the bounds of
        /// its module's meshes, or the socket with the marker's size when it is empty.
        /// </summary>
        private void MeasureSlots()
        {
            _slotTargets.Clear();
            _slotBounds.Clear();
            _slotYaws.Clear();
            _slotElevations.Clear();
            for (var slot = 0; slot < _slotSockets.Count; slot++)
            {
                var socket = _slotSockets[slot];
                var empty = socket != null ? turntable.InverseTransformPoint(socket.transform.position) : Vector3.zero;
                var bounds = new Bounds(empty, Vector3.one * _settings.SocketMarkerSize);
                var found = false;
                foreach (var part in _parts)
                {
                    if (part.Slot != slot || part.Renderer == null || !part.Renderer.TryGetComponent<MeshFilter>(out var filter) ||
                        filter.sharedMesh == null) continue;
                    var local = filter.sharedMesh.bounds;
                    for (var corner = 0; corner < 8; corner++)
                    {
                        var point = local.center + Vector3.Scale(local.extents,
                            new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                        point = turntable.InverseTransformPoint(part.Renderer.transform.TransformPoint(point));
                        if (!found) bounds = new Bounds(point, Vector3.zero);
                        else bounds.Encapsulate(point);
                        found = true;
                    }
                }
                var radius = Mathf.Max(bounds.extents.magnitude, MinRadius);
                if (socket != null && socket.Slot == SlotType.Armour) radius = Mathf.Max(radius, ArmourFraction * _size);
                _slotTargets.Add((bounds.center, radius));
                _slotBounds.Add(found ? bounds : null);
            }
            _rotorBottom = null;
            foreach (var bounds in _slotBounds)
                if (bounds is Bounds rotor && IsTop(rotor.center))
                    _rotorBottom = Mathf.Min(_rotorBottom ?? float.MaxValue, rotor.min.y);
            // Mesh colliders on the body and the modules while the views are measured, then removed. They use the
            // library's readable copies (Setup step 7): the kit meshes are not readable in a build. Rotors do not count
            // (thin blades that turn).
            _bodyColliders.Clear();
            foreach (var part in _parts)
            {
                if (part.Renderer == null || !part.Renderer.TryGetComponent<MeshFilter>(out var filter) ||
                    _settings.Wireframes == null || !_settings.Wireframes.TryGetBody(filter.sharedMesh, out var body)) continue;
                if (part.Slot >= 0 && part.Slot < _slotBounds.Count && _slotBounds[part.Slot] is Bounds module && IsTop(module.center)) continue;
                var collider = part.Renderer.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = body;
                _bodyColliders.Add(collider, part.Slot);
            }
            Physics.SyncTransforms();
            for (var slot = 0; slot < _slotTargets.Count; slot++)
            {
                var (yaw, elevation) = ClearView(slot);
                _slotYaws.Add(yaw);
                _slotElevations.Add(elevation);
            }
            foreach (var collider in _bodyColliders.Keys) Destroy(collider);
            _bodyColliders.Clear();
        }

        /// <summary>
        /// The view of a slot (turntable yaw and camera elevation): the first of <see cref="Sides"/> where the camera
        /// sees the module (its centre and four points round it) past the body and the other modules (a rocket pod or
        /// an armour tank in front of the flare dispenser); then the same from steeply below (modules under the
        /// chopper); else the view that sees the most of it. Rotors do not count (thin blades that turn).
        /// </summary>
        private (float Yaw, float Elevation) ClearView(int slot)
        {
            var (target, radius) = _slotTargets[slot];
            LookPoints(slot, target);
            Sides(target);
            var elevation = FocusElevationFor(target, radius);
            var best = (YawOf(_sides[0]), elevation);
            var fewest = int.MaxValue;
            for (var pass = 0; pass < 2 && fewest > 0; pass++)
            {
                if (pass == 1)
                {
                    // Only modules in the bottom half get the view from below.
                    if (target.y >= 0f) break;
                    elevation = SteepBelow;
                }
                var offset = FocusOffset(radius, elevation);
                foreach (var side in _sides)
                {
                    var yaw = YawOf(side);
                    // Camera position on the turntable (yaw 0) when the turntable is at this yaw.
                    var camera = target + Quaternion.Euler(0f, -yaw, 0f) * offset;
                    var blocked = 0;
                    foreach (var point in _lookPoints)
                        if (Blocked(slot, camera, point)) blocked++;
                    if (blocked >= fewest) continue;
                    fewest = blocked;
                    best = (yaw, elevation);
                    if (blocked == 0) break;
                }
            }
            return best;
        }

        /// <summary>The module's centre and four points round it (half way to its sides), turntable space.</summary>
        private void LookPoints(int slot, Vector3 target)
        {
            _lookPoints.Clear();
            _lookPoints.Add(target);
            if (_slotBounds[slot] is not Bounds bounds) return;
            var extents = bounds.extents * 0.5f;
            _lookPoints.Add(target + new Vector3(extents.x, 0f, 0f));
            _lookPoints.Add(target - new Vector3(extents.x, 0f, 0f));
            _lookPoints.Add(target + new Vector3(0f, 0f, extents.z));
            _lookPoints.Add(target - new Vector3(0f, 0f, extents.z));
        }

        /// <summary>True when the body or another module is on the line from the camera to the point (turntable space).</summary>
        private bool Blocked(int slot, Vector3 camera, Vector3 point)
        {
            const float Slack = 0.15f;
            if (_bodyColliders.Count == 0) return false;
            var from = turntable.TransformPoint(camera);
            var to = turntable.TransformPoint(point);
            var direction = to - from;
            var reach = direction.magnitude - Slack;
            if (reach <= 0f) return false;
            var count = Physics.RaycastNonAlloc(from, direction.normalized, _hits, reach, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
                if (_hits[i].collider is MeshCollider mesh && _bodyColliders.TryGetValue(mesh, out var owner) && owner != slot)
                    return true;
            return false;
        }

        /// <summary>
        /// Views to try for a module at <paramref name="local"/> (x = which side, y = how much of the front: a
        /// direction on the ground, turntable space), best first. Decided by the side its centre is on, not by its
        /// distance from the centre (the rotor disc makes the chopper wide, so pylons and stub wings are near the
        /// middle by that measure):
        /// <list type="bullet">
        /// <item>left or right of the centre line (pylons, side guns): that side, a little from the front; then from
        /// a little behind, more from the front, more from behind, straight on;</item>
        /// <item>on the centre line, at the front or back (nose, tail): a 3/4 view from the front or back, left side
        /// first;</item>
        /// <item>in the middle (rotor mast, armour, engine): the front left (back left when the module sits behind the
        /// centre: engines), then the other corners.</item>
        /// </list>
        /// </summary>
        private void Sides(Vector3 local)
        {
            _sides.Clear();
            var lateral = Mathf.Max(LateralMinimum, LateralFraction * _size);
            if (Mathf.Abs(local.x) > lateral)
            {
                var x = Mathf.Sign(local.x);
                _sides.Add(new Vector2(x, FrontBias));
                _sides.Add(new Vector2(x, -FrontBias));
                _sides.Add(new Vector2(x, 1f));
                _sides.Add(new Vector2(x, -1f));
                _sides.Add(new Vector2(x, 0f));
            }
            else if (Mathf.Abs(local.z) > 0.15f * _size)
            {
                var z = Mathf.Sign(local.z);
                _sides.Add(new Vector2(-SideBias, z));
                _sides.Add(new Vector2(SideBias, z));
                _sides.Add(new Vector2(-0.3f, z));
            }
            else
            {
                // The corner on the module's own end first: engines sit behind the mast, so from behind.
                var z = local.z < -BackOffset ? -0.6f : 0.6f;
                _sides.Add(new Vector2(-1f, z));
                _sides.Add(new Vector2(1f, z));
                _sides.Add(new Vector2(-1f, -z));
                _sides.Add(new Vector2(1f, -z));
            }
        }

        /// <summary>Turntable yaw that turns the side <paramref name="side"/> (x, z on the turntable) to the camera (-z).</summary>
        private static float YawOf(Vector2 side) => 180f - Mathf.Atan2(side.x, side.y) * Mathf.Rad2Deg;

        /// <summary>On top of the chopper (rotor): seen from high above.</summary>
        private bool IsTop(Vector3 local) => local.y > TopFraction * _halfHeight;

        /// <summary>
        /// Degrees the camera looks down at a module: from above in the top half and from a little below in the bottom
        /// half. Below the rotor the camera stays under the rotor disc, so the blades do not cover the module (engine).
        /// The rotor itself does not use this (<see cref="CameraTarget"/>).
        /// </summary>
        private float FocusElevationFor(Vector3 target, float radius)
        {
            var elevation = target.y < 0f ? _settings.FocusElevationBelow : _settings.FocusElevation;
            if (!IsTop(target) && _rotorBottom is float bottom)
            {
                var distance = FocusDistanceFor(radius);
                var highest = Mathf.Asin(Mathf.Clamp((bottom - RotorClearance - target.y) / distance, -1f, 1f)) * Mathf.Rad2Deg;
                elevation = Mathf.Max(Mathf.Min(elevation, highest), _settings.FocusElevationBelow);
            }
            return elevation;
        }

        /// <summary>Camera position from the module (turntable space, before the yaw).</summary>
        private Vector3 FocusOffset(float radius, float elevation)
        {
            elevation *= Mathf.Deg2Rad;
            return new Vector3(0f, Mathf.Sin(elevation), -Mathf.Cos(elevation)) * FocusDistanceFor(radius);
        }

        /// <summary>Camera distance at which a module of this radius fills <see cref="HangarSettings.FocusFill"/> of the view.</summary>
        private float FocusDistanceFor(float radius)
        {
            var halfView = Mathf.Tan(0.5f * _settings.FocusFieldOfView * Mathf.Deg2Rad);
            return Mathf.Clamp(radius / (_settings.FocusFill * halfView),
                _settings.FocusMinDistance, Mathf.Max(_settings.FocusMinDistance, _size * _settings.FocusDistance));
        }

        private void ApplyLook()
        {
            _block ??= new MaterialPropertyBlock();
            var skins = _context.Config.Skins;
            var row = skins != null ? skins.IndexOf(_model.SkinId) : 0;
            foreach (var part in _parts)
            {
                if (part.Renderer == null) continue;
                part.Renderer.GetPropertyBlock(_block);
                _block.SetFloat(Visuals.SkinLibrary.SkinIndexProperty, row);
                part.Renderer.SetPropertyBlock(_block);
            }
            ApplyPartLooks(Time.unscaledTime);
        }

        /// <summary>
        /// Before the scan reaches a part: hidden. Then its lines glow (scan material) for a moment. Else the focused
        /// slot's module glows (highlight) and the rest is a faint overlay on the paint.
        /// </summary>
        private void ApplyPartLooks(float time)
        {
            foreach (var part in _parts)
            {
                if (part.Renderer == null) continue;
                var hidden = time < part.RevealAt;
                part.Renderer.enabled = part.Shown && !hidden;
                if (part.Wire == null) continue;
                var scanning = !hidden && time < part.GlowUntil && _settings.Scan != null;
                var focused = part.Slot >= 0 && part.Slot == _model.FocusedSlot;
                part.Wire.sharedMaterial = scanning ? _settings.Scan : focused ? _settings.Highlight : _settings.Overlay;
                part.Wire.enabled = !hidden && (scanning || focused || _settings.Overlay != null);
            }
        }

        /// <summary>Fitted modules ease onto their sockets (fast, then slow).</summary>
        private void ApplySlides(float time)
        {
            for (var i = _slides.Count - 1; i >= 0; i--)
            {
                var (visual, rest, from, start) = _slides[i];
                if (visual == null)
                {
                    _slides.RemoveAt(i);
                    continue;
                }
                var t = Mathf.Clamp01((time - start) / _settings.FitSlideSeconds);
                var ease = 1f - (1f - t) * (1f - t) * (1f - t);
                visual.localPosition = Vector3.LerpUnclamped(from, rest, ease);
                if (t >= 1f) _slides.RemoveAt(i);
            }
        }

        /// <summary>
        /// Player turning: drag or stick input this frame; after a drag lets go the chopper keeps turning and slows
        /// down. Returns the degrees to turn now.
        /// </summary>
        private float TakeTurn(float deltaTime)
        {
            float degrees;
            if (_turnInput != 0f)
            {
                degrees = _turnInput;
                if (_turnFlings && deltaTime > 0f)
                    _turnVelocity = Mathf.Lerp(_turnVelocity, _turnInput / deltaTime, 0.5f);
                else _turnVelocity = 0f;
                _spinWait = _settings.SpinResumeDelay;
            }
            else if (Holding)
            {
                degrees = 0f;
                _turnVelocity = 0f;
                _spinWait = _settings.SpinResumeDelay;
            }
            else
            {
                degrees = _turnVelocity * deltaTime;
                _turnVelocity *= Mathf.Exp(-_settings.FlingDamping * deltaTime);
                if (Mathf.Abs(_turnVelocity) < 1f) _turnVelocity = 0f;
                _spinWait = Mathf.Max(0f, _spinWait - deltaTime);
            }
            _turnInput = 0f;
            return degrees;
        }

        private void Update()
        {
            var deltaTime = Time.unscaledDeltaTime;
            var blend = 1f - Mathf.Exp(-BlendRate / _settings.FocusBlendSeconds * deltaTime);
            var focus = FocusedSocket();

            // The idle spin eases to a stop while the pointer is near the chopper (so the callout cards hold still)
            // and for a while after the player turned it.
            var turn = TakeTurn(deltaTime);
            var spinTarget = Holding || _spinWait > 0f || PointerNearChopper() ? 0f : _settings.IdleSpin;
            _spin = Mathf.Lerp(_spin, spinTarget, 1f - Mathf.Exp(-BlendRate / _settings.SpinEaseSeconds * deltaTime));
            if (focus != null)
            {
                var limit = _settings.FocusTurnLimit;
                var turned = Mathf.Clamp(_focusTurn + turn, -limit, limit);
                if (Mathf.Abs(turned) >= limit) _turnVelocity = 0f;
                // The drag follows the pointer at once; the blend only eases to a new slot.
                _yaw += turned - _focusTurn;
                _focusTurn = turned;
                _yaw += Mathf.DeltaAngle(_yaw, FocusYaw(focus) + _focusTurn) * blend;
            }
            else _yaw += turn + _spin * deltaTime;
            _yaw = Mathf.Repeat(_yaw, 360f);
            turntable.localRotation = Quaternion.Euler(0f, _yaw, 0f);

            CameraTarget(focus, out var position, out var rotation, out var fieldOfView);
            var view = viewCamera.transform;
            view.position = Vector3.Lerp(view.position, position, blend);
            view.rotation = Quaternion.Slerp(view.rotation, rotation, blend);
            viewCamera.fieldOfView = Mathf.Lerp(viewCamera.fieldOfView, fieldOfView, blend);

            var time = Time.unscaledTime;
            if (time <= _animationEnd + deltaTime)
            {
                ApplyPartLooks(time);
                ApplySlides(time);
            }
            foreach (var spinner in _spinners)
            {
                if (spinner.Visual == null) continue;
                // Still while it builds or while any slot is open (the zoomed view stays calm); ramps down and up over the spin-up time.
                var hold = time < spinner.HoldUntil || _model.FocusedSlot != HangarModel.Overview;
                var ramp = _settings.RotorSpinUpSeconds > 0f ? deltaTime / _settings.RotorSpinUpSeconds : 1f;
                spinner.Speed = Mathf.MoveTowards(spinner.Speed, hold ? 0f : 1f, ramp);
                spinner.Phase = (spinner.Phase + Mathf.SmoothStep(0f, 1f, spinner.Speed) * deltaTime) % 3600f;
                spinner.Visual.localRotation = spinner.Rest * Quaternion.Euler(spinner.Spin * spinner.Phase);
            }

            var pulse = 1f + 0.25f * Mathf.Sin(time * _settings.SocketPulseRate * 2f * Mathf.PI);
            foreach (var (marker, slot) in _markers)
            {
                var size = _settings.SocketMarkerSize * (slot == _model.FocusedSlot ? 1.6f : 1f) * pulse;
                marker.transform.localScale = Vector3.one * size;
            }
        }

        private SocketAuthoring FocusedSocket()
        {
            var slot = _model.FocusedSlot;
            return slot >= 0 && slot < _slotSockets.Count ? _slotSockets[slot] : null;
        }

        /// <summary>
        /// Overview: in front, a little above, the whole chopper in view. Slot: the camera looks at the slot's module
        /// from the side it is on (<see cref="FocusYaw"/>), from above (or a little below for the bottom half), at a
        /// distance that makes the module fill <see cref="HangarSettings.FocusFill"/> of the view. A rotor keeps the
        /// overview angle and the chopper does not turn: the camera only moves back until the disc fits
        /// (<see cref="HangarSettings.RotorFill"/>). The pose is taken with the turntable already at the focus yaw, so the camera does not chase the
        /// turning model. Both put their target at <see cref="HangarSettings.ViewCentre"/>: the middle of the gap
        /// between the hangar panels, not the middle of the screen.
        /// </summary>
        private void CameraTarget(SocketAuthoring focus, out Vector3 position, out Quaternion rotation, out float fieldOfView)
        {
            var centre = turntable.position;
            Vector3 lookAt;
            if (focus == null)
            {
                lookAt = centre;
                position = centre + new Vector3(0f, _size * _settings.OverviewHeight, -_size * _settings.OverviewDistance);
                fieldOfView = _settings.OverviewFieldOfView;
                rotation = Frame(Quaternion.LookRotation(lookAt - position), fieldOfView);
                return;
            }

            var (target, radius) = FocusTarget(focus);
            lookAt = centre + Quaternion.Euler(0f, FocusYaw(focus) + _focusTurn, 0f) * target;
            if (_settings.UseSocketPoses)
            {
                position = lookAt + focus.FocusCameraOffset;
                fieldOfView = focus.FocusFieldOfView;
                rotation = Frame(Quaternion.LookRotation(lookAt - position), fieldOfView);
                return;
            }

            fieldOfView = _settings.FocusFieldOfView;
            if (IsTop(target))
            {
                // Rotor: no turn and no new angle; the overview camera aims between the chopper's centre and the rotor
                // and moves back until the whole disc and the body fit.
                var middle = 0.5f * target.y;
                lookAt = centre + Vector3.up * middle;
                var halfWidth = Mathf.Tan(0.5f * fieldOfView * Mathf.Deg2Rad) * viewCamera.aspect;
                var fit = (radius + middle) / (_settings.RotorFill * halfWidth);
                var overview = new Vector3(0f, _settings.OverviewHeight, -_settings.OverviewDistance).normalized;
                position = lookAt + overview * Mathf.Max(fit, _settings.FocusMinDistance);
                rotation = Frame(Quaternion.LookRotation(lookAt - position), fieldOfView);
                return;
            }

            // The turntable turns the module's side to the camera (-z).
            var slot = _slotSockets.IndexOf(focus);
            var elevation = slot >= 0 && slot < _slotElevations.Count ? _slotElevations[slot] : FocusElevationFor(target, radius);
            position = lookAt + FocusOffset(radius, elevation);
            rotation = Frame(Quaternion.LookRotation(lookAt - position), fieldOfView);
        }

        /// <summary>
        /// Turns a camera that looks straight at its target so the target shows at <see cref="HangarSettings.ViewCentre"/>
        /// (viewport, y up) instead of the screen centre.
        /// </summary>
        private Quaternion Frame(Quaternion look, float fieldOfView)
        {
            var point = _settings.ViewCentre;
            var halfV = Mathf.Tan(0.5f * fieldOfView * Mathf.Deg2Rad);
            var halfH = halfV * viewCamera.aspect;
            // Left of centre = turn right; below centre = pitch up (negative x).
            var yaw = -Mathf.Atan((point.x - 0.5f) * 2f * halfH) * Mathf.Rad2Deg;
            var pitch = Mathf.Atan((point.y - 0.5f) * 2f * halfV) * Mathf.Rad2Deg;
            return look * Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>
        /// Smallest radius a module counts as: <see cref="HangarSettings.FocusMinRadius"/>, or a part of the chopper's
        /// length for big chassis (Lifter), so small parts keep some chopper round them.
        /// </summary>
        private float MinRadius => Mathf.Max(_settings.FocusMinRadius, _settings.FocusMinRadiusFraction * _size);

        /// <summary>The slot's focus target (turntable space at yaw 0) and its radius.</summary>
        private (Vector3 Centre, float Radius) FocusTarget(SocketAuthoring socket)
        {
            var slot = _slotSockets.IndexOf(socket);
            return slot >= 0 && slot < _slotTargets.Count
                ? _slotTargets[slot]
                : (SocketOnTurntable(socket), MinRadius);
        }

        /// <summary>Socket position on the turntable with the turntable at yaw 0.</summary>
        private Vector3 SocketOnTurntable(SocketAuthoring socket) =>
            Quaternion.Inverse(turntable.localRotation) * (socket.transform.position - turntable.position);

        /// <summary>Turntable yaw that shows the slot's module (<see cref="ClearView"/>, measured when the chopper is built).</summary>
        private float FocusYaw(SocketAuthoring socket)
        {
            if (_settings.UseSocketPoses) return socket.FocusChopperYaw;
            if (IsTop(FocusTarget(socket).Centre)) return _topYaw;
            var slot = _slotSockets.IndexOf(socket);
            if (slot >= 0 && slot < _slotYaws.Count) return _slotYaws[slot];
            Sides(SocketOnTurntable(socket));
            return YawOf(_sides[0]);
        }

        /// <summary>
        /// True while the mouse is over the chopper on screen, or within <see cref="HangarSettings.SpinHoldMargin"/> of
        /// it (the callout cards sit there).
        /// </summary>
        private bool PointerNearChopper()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || _parts.Count == 0) return false;
            var pointer = viewCamera.ScreenToViewportPoint(mouse.position.ReadValue());
            if (pointer.x < 0f || pointer.x > 1f || pointer.y < 0f || pointer.y > 1f) return false;

            var bounds = new Bounds();
            var first = true;
            foreach (var part in _parts)
            {
                if (part.Renderer == null) continue;
                if (first) bounds = part.Renderer.bounds;
                else bounds.Encapsulate(part.Renderer.bounds);
                first = false;
            }
            if (first) return false;

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var corner = 0; corner < 8; corner++)
            {
                var point = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                var viewport = viewCamera.WorldToViewportPoint(point);
                if (viewport.z <= 0f) continue;
                min = Vector2.Min(min, viewport);
                max = Vector2.Max(max, viewport);
            }
            var margin = _settings.SpinHoldMargin;
            return pointer.x >= min.x - margin && pointer.x <= max.x + margin &&
                   pointer.y >= min.y - margin && pointer.y <= max.y + margin;
        }

        private void SnapCamera()
        {
            CameraTarget(null, out var position, out var rotation, out var fieldOfView);
            viewCamera.transform.SetPositionAndRotation(position, rotation);
            viewCamera.fieldOfView = fieldOfView;
        }

        private void OnLaunched()
        {
            if (_launching) return;
            // Training: the map picked in the hangar (MAP list).
            var training = !string.IsNullOrEmpty(_model.TrainingScene);
            var scene = training ? _model.TrainingScene : matchScene;
            var names = _model.TrainingMapNames;
            var label = training && _model.TrainingMapIndex < names.Count ? $"TRAINING // {names[_model.TrainingMapIndex]}" : null;
            // The local session starts once the screen is covered (false when worlds already run: a match scene opened
            // the hangar), then the scene loads behind the transition.
            _launching = _context.LoadScene(scene, () => LocalSession.Start(), label);
        }

        private void OnMatchRequested() => FindMatchAsync().Forget();

        private void OnSearchCancelled()
        {
            _search?.Cancel();
        }

        /// <summary>Online: matchmaker ticket → Edgegap server → client-only worlds connect to it → the match scene.</summary>
        private async UniTaskVoid FindMatchAsync()
        {
            if (_matchmaking == null)
            {
                _model.SetMatchStatus("ONLINE PLAY IS NOT SET UP (NO MATCHMAKING SERVICE)", false);
                return;
            }
            _search?.Dispose();
            _search = new CancellationTokenSource();
            var token = CancellationTokenSource.CreateLinkedTokenSource(_search.Token, destroyCancellationToken).Token;
            var modules = new List<ushort>();
            foreach (var module in _model.Fitted) modules.Add(module != null ? module.Id : (ushort)0);
            var request = new MatchRequest(_settings.MatchQueue, _model.Chassis.Id, modules, _model.Pilots.SelectedId);
            var progress = new Progress<MatchmakingStatus>(status => _model.SetMatchStatus(status switch
            {
                MatchmakingStatus.ServerStarting => "MATCH FOUND - SERVER STARTING",
                MatchmakingStatus.Connecting => "CONNECTING",
                _ => "SEARCHING",
            }, true));

            try
            {
                var assignment = await _matchmaking.FindMatchAsync(request, progress, token);
                // The hangar stays usable while searching: spawn with the loadout as it is now (if still valid).
                _model.SaveChoice();
                if (!LocalSession.Join(assignment.Address, assignment.Port))
                {
                    _model.SetMatchStatus($"CANNOT CONNECT TO {assignment.Address}:{assignment.Port}", false);
                    return;
                }
                _launching = true;
                // The server picked the map (random from the match pool); it tells us on connect.
                _model.SetMatchStatus("LOADING MAP", true);
                var scene = await AnnouncedMapScene(token);
                _context.LoadScene(scene, label: "MATCH // " + SceneFlow.Pretty(scene));
            }
            catch (OperationCanceledException)
            {
                if (_launching) LocalSession.Stop();       // cancelled while waiting for the map: leave the server
                _launching = false;
                if (this != null) _model.SetMatchStatus("", false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Matchmaking failed: {e}");
                if (this != null) _model.SetMatchStatus(e.Message.ToUpperInvariant(), false);
            }
        }

        /// <summary>Seconds to wait for the server's map before loading <see cref="matchScene"/> (an older server build).</summary>
        private const float MapAnnouncementTimeout = 20f;

        /// <summary>The scene of the map the joined server announced (<see cref="AnnouncedMap"/>, sent on connect).</summary>
        private async UniTask<string> AnnouncedMapScene(CancellationToken token)
        {
            var maps = _context.Config != null ? _context.Config.Maps : null;
            var until = Time.realtimeSinceStartup + MapAnnouncementTimeout;
            while (Time.realtimeSinceStartup < until)
            {
                token.ThrowIfCancellationRequested();
                foreach (var world in Unity.Entities.World.All)
                {
                    if (!world.IsCreated || !Unity.NetCode.ClientServerWorldExtensions.IsClient(world)) continue;
                    using var query = world.EntityManager.CreateEntityQuery(typeof(AnnouncedMap));
                    if (query.IsEmpty) continue;
                    var id = query.GetSingleton<AnnouncedMap>().MapId;
                    var map = maps != null ? maps.Find(id) : null;
                    if (map != null) return map.Scene;
                    Debug.LogWarning($"Server map {id} is not in this build's map catalog; loading {matchScene}.");
                    return matchScene;
                }
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
            Debug.LogWarning($"The server did not say its map in {MapAnnouncementTimeout} s; loading {matchScene}.");
            return matchScene;
        }

        /// <summary>Wire octahedron (unit size) for empty sockets.</summary>
        private static Mesh MarkerMesh()
        {
            var points = new[]
            {
                new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 0f, 0f), new Vector3(0f, 0.5f, 0f),
                new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0f, 0.5f), new Vector3(0f, 0f, -0.5f),
            };
            var lines = new[] { 0, 2, 0, 3, 0, 4, 0, 5, 1, 2, 1, 3, 1, 4, 1, 5, 2, 4, 4, 3, 3, 5, 5, 2 };
            var mesh = new Mesh { name = "Socket Marker" };
            mesh.SetVertices(points);
            mesh.SetIndices(lines, MeshTopology.Lines, 0);
            return mesh;
        }
    }
}
