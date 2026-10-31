using System;
using System.Collections.Generic;
using System.Threading;
using Chopper.Presentation.Bridge;
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
    /// module prefabs as the game ("what you see is what flies"). Green wireframe on a dark fill (lines baked in the
    /// <see cref="WireframeLibrary"/>); the selected slot's module in the highlight colour; empty sockets pulse; or the
    /// real paint with the chosen skin (<see cref="HangarModel.PaintView"/>). The camera and turntable blend to the
    /// selected socket's focus pose (<see cref="SocketAuthoring"/>); the overview turns slowly. Launch starts a local
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

        [SerializeField, Tooltip("Scene presentation bridge; it owns the hangar model.")] private PresentationBridge bridge;
        [SerializeField] private UnityEngine.Camera viewCamera;
        [SerializeField, Tooltip("The chopper turns on this.")] private Transform turntable;
        [SerializeField, Tooltip("Scene loaded by launch.")] private string matchScene = ChopperBootstrap.TrainingRangeScene;

        private sealed class Part
        {
            public Renderer Renderer;
            public Material[] Paint;
            public Material[] Fill;
            public MeshRenderer Wire;
            /// <summary>Loadout slot of the module (-1 = the chassis).</summary>
            public int Slot;
        }

        private readonly List<Part> _parts = new();
        private readonly List<(Transform Visual, Quaternion Start, Vector3 Spin)> _spinners = new();
        /// <summary>Socket of each loadout slot (null when the model has none).</summary>
        private readonly List<SocketAuthoring> _slotSockets = new();
        private readonly List<(GameObject Marker, int Slot)> _markers = new();
        private readonly Dictionary<SlotType, int> _ordinals = new();
        private PresentationContext _context;
        private HangarModel _model;
        private HangarSettings _settings;
        private GameObject _chopper;
        private ChassisDefinition _builtChassis;
        private ModuleDefinition[] _builtModules = System.Array.Empty<ModuleDefinition>();
        private float _size = 10f;
        private float _yaw = 150f;
        private MaterialPropertyBlock _block;
        private Mesh _markerMesh;
        private bool _launching;
        private IMatchmakingService _matchmaking;
        private CancellationTokenSource _search;

        /// <summary>Online play: set by the hangar scene's lifetime scope (the root scope's matchmaking service).</summary>
        public void UseMatchmaking(IMatchmakingService matchmaking) => _matchmaking = matchmaking;

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
            ApplyLook();
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

            var chassis = _model.Chassis;
            var fitted = _model.Fitted;
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
            if (module.VisualSpin != Vector3.zero)
                _spinners.Add((visual.transform, visual.transform.localRotation, module.VisualSpin * (socket.Index % 2 == 0 ? 1f : -1f)));
        }

        /// <summary>Each mesh keeps its paint (paint view) and gets a fill and its baked line mesh (wireframe view).</summary>
        private void AddParts(GameObject root, int slot)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name == WireName || !filter.TryGetComponent<Renderer>(out var target)) continue;
                var paint = target.sharedMaterials;
                var fill = new Material[paint.Length];
                for (var i = 0; i < fill.Length; i++) fill[i] = _settings.Fill;

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
                _parts.Add(new Part { Renderer = target, Paint = paint, Fill = fill, Wire = wire, Slot = slot });
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
        }

        private void ApplyLook()
        {
            _block ??= new MaterialPropertyBlock();
            var paint = _model.PaintView;
            var skins = _context.Config.Skins;
            var row = skins != null ? skins.IndexOf(_model.SkinId) : 0;
            foreach (var part in _parts)
            {
                if (part.Renderer == null) continue;
                part.Renderer.sharedMaterials = paint ? part.Paint : part.Fill;
                part.Renderer.GetPropertyBlock(_block);
                _block.SetFloat(Visuals.SkinLibrary.SkinIndexProperty, row);
                part.Renderer.SetPropertyBlock(_block);
                if (part.Wire == null) continue;
                part.Wire.enabled = !paint;
                part.Wire.sharedMaterial = part.Slot >= 0 && part.Slot == _model.FocusedSlot ? _settings.Highlight : _settings.Line;
            }
            foreach (var (marker, _) in _markers) marker.SetActive(!paint);
        }

        private void Update()
        {
            var deltaTime = Time.unscaledDeltaTime;
            var blend = 1f - Mathf.Exp(-BlendRate / _settings.FocusBlendSeconds * deltaTime);
            var focus = FocusedSocket();

            _yaw = focus != null
                ? _yaw + Mathf.DeltaAngle(_yaw, FocusYaw(focus)) * blend
                : _yaw + _settings.IdleSpin * deltaTime;
            _yaw = Mathf.Repeat(_yaw, 360f);
            turntable.localRotation = Quaternion.Euler(0f, _yaw, 0f);

            CameraTarget(focus, out var position, out var lookAt, out var fieldOfView);
            var view = viewCamera.transform;
            view.position = Vector3.Lerp(view.position, position, blend);
            view.rotation = Quaternion.Slerp(view.rotation, Quaternion.LookRotation(lookAt - position), blend);
            viewCamera.fieldOfView = Mathf.Lerp(viewCamera.fieldOfView, fieldOfView, blend);

            var time = Time.unscaledTime;
            foreach (var (visual, rest, spin) in _spinners)
                if (visual != null) visual.localRotation = rest * Quaternion.Euler(spin * (time % 3600f));

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

        /// <summary>Overview: in front, a little above, the whole chopper in view. Slot: the socket's focus pose, taken
        /// with the turntable already at the focus yaw, so the camera does not chase the turning model.</summary>
        private void CameraTarget(SocketAuthoring focus, out Vector3 position, out Vector3 lookAt, out float fieldOfView)
        {
            var centre = turntable.position;
            if (focus == null)
            {
                lookAt = centre;
                position = centre + new Vector3(0f, _size * _settings.OverviewHeight, -_size * _settings.OverviewDistance);
                fieldOfView = _settings.OverviewFieldOfView;
                return;
            }

            lookAt = centre + Quaternion.Euler(0f, FocusYaw(focus), 0f) * SocketOnTurntable(focus);
            if (_settings.UseSocketPoses)
            {
                position = lookAt + focus.FocusCameraOffset;
                fieldOfView = focus.FocusFieldOfView;
                return;
            }

            // The turntable turns the socket's side to the camera (-z); look at it from there: from above for the top
            // half, from a little below for the bottom half.
            var below = SocketOnTurntable(focus).y < 0f;
            var elevation = (below ? _settings.FocusElevationBelow : _settings.FocusElevation) * Mathf.Deg2Rad;
            var distance = _size * _settings.FocusDistance;
            position = lookAt + new Vector3(0f, Mathf.Sin(elevation), -Mathf.Cos(elevation)) * distance;
            fieldOfView = _settings.FocusFieldOfView;
        }

        /// <summary>Socket position on the turntable with the turntable at yaw 0.</summary>
        private Vector3 SocketOnTurntable(SocketAuthoring socket) =>
            Quaternion.Inverse(turntable.localRotation) * (socket.transform.position - turntable.position);

        /// <summary>Turntable yaw that shows the socket: its side faces the camera. A socket near the middle (rotor
        /// mast, armour) is shown from the front left.</summary>
        private float FocusYaw(SocketAuthoring socket)
        {
            if (_settings.UseSocketPoses) return socket.FocusChopperYaw;
            var local = SocketOnTurntable(socket);
            var side = new Vector2(local.x, local.z);
            if (side.magnitude < 0.15f * _size) side = new Vector2(-1f, 0.6f);
            return 180f - Mathf.Atan2(side.x, side.y) * Mathf.Rad2Deg;
        }

        private void SnapCamera()
        {
            CameraTarget(null, out var position, out var lookAt, out var fieldOfView);
            viewCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
            viewCamera.fieldOfView = fieldOfView;
        }

        private void OnLaunched()
        {
            if (_launching) return;
            _launching = true;
            // False when worlds already run (a match scene opened the hangar): just load the scene.
            LocalSession.Start();
            SceneManager.LoadScene(matchScene);
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
            var request = new MatchRequest(_settings.MatchQueue, _model.Chassis.Id, modules);
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
                SceneManager.LoadScene(matchScene);
            }
            catch (OperationCanceledException)
            {
                if (this != null) _model.SetMatchStatus("", false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Matchmaking failed: {e}");
                if (this != null) _model.SetMatchStatus(e.Message.ToUpperInvariant(), false);
            }
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
