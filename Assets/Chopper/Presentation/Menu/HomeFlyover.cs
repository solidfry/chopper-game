using Chopper.Presentation.Bridge;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chopper.Presentation.Menu
{
    /// <summary>
    /// Home screen backdrop: loads a city scene (<see cref="sceneName"/>: HomeCity, a simplified City04 from Setup step
    /// 11) under the menu and flies this camera in a slow orbit round the middle of its buildings, always aimed at the
    /// city centre. The centre is the median building (a few outliers do not pull it off); the orbit radius is the
    /// distance that holds most buildings, so the camera stays over the crater floor. The ground keeps its colliders
    /// in HomeCity: the camera pulls in when a wall would come between it and the centre, and stays clear of the ground.
    /// The terminal look comes from the home post Volumes (Setup step 11) and the dark green fog set here; the home UI
    /// lays a CRT layer over it (<see cref="HomeModel.LiveBackdrop"/>). Without the scene in the build the drawn grid
    /// stays. Put it on the home camera. The scene goes away with the home scene (single scene load).
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    [DefaultExecutionOrder(100)] // after HomeSkirmish moves the choppers, so follow shots do not lag a frame
    public sealed class HomeFlyover : MonoBehaviour
    {
        [SerializeField, Tooltip("Scene presentation bridge; it owns the home model.")]
        private PresentationBridge bridge;
        [SerializeField, Tooltip("City scene (in the build settings). Empty = drawn grid only.")]
        private string sceneName = "HomeCity";
        [SerializeField, Tooltip("Root object whose children are the buildings (orbit centre and size).")]
        private string focusRoot = "Buildings";
        [SerializeField, Range(0.3f, 1f), Tooltip("Share of buildings inside the orbit: sets the radius.")]
        private float radiusShare = 0.95f;
        [SerializeField, Tooltip("Orbit radius × this (smaller = closer).")]
        private float radiusScale = 1.8f;
        [SerializeField, Tooltip("Camera height above the city ground, metres.")]
        private float height = 650f;
        [SerializeField, Tooltip("Height of the aim point above the city ground, metres.")]
        private float lookHeight = 0f;
        [SerializeField, Tooltip("Field of view, degrees.")]
        private float fieldOfView = 50f;
        [SerializeField, Range(0f, 0.8f), Tooltip("Where the city centre sits across the screen: 0 = middle, 0.4 = well to the right (clear of the menu).")]
        private float screenOffset = 0.38f;
        [SerializeField, Range(0, 2), Tooltip("Rings of city copies round the core (1 = 8 copies): a sprawl to the horizon.")]
        private int sprawlRings = 1;
        [SerializeField, Tooltip("Choppers to film (chase, fly-by, tracking shots). Empty = city shots only.")]
        private HomeSkirmish skirmish;
        [SerializeField, Tooltip("Seconds per shot, min and max: then a hard cut.")]
        private Vector2 shotSeconds = new(4f, 7f);
        [SerializeField, Tooltip("Seconds per full orbit.")]
        private float orbitSeconds = 240f;
        [SerializeField, Tooltip("Up and down drift, metres.")]
        private float bob = 20f;
        [SerializeField, Tooltip("Closest the camera comes to the ground or a wall, metres.")]
        private float clearance = 40f;
        [SerializeField] private Color fogColour = new(0.01f, 0.05f, 0.03f);
        [SerializeField] private float fogDensity = 0.00045f;
        [SerializeField, Tooltip("Blur and darken Volume (weight 0 at rest); faded in while a submenu is open.")]
        private UnityEngine.Rendering.Volume focusVolume;
        [SerializeField, Tooltip("Seconds to fade the blur in or out.")]
        private float focusFadeSeconds = 0.25f;

        [Header("Pixel look")]
        [SerializeField, Tooltip("Hidden/Chopper/PixelQuantize. Empty = full resolution, straight to the screen.")]
        private Shader quantizeShader;
        [SerializeField, Range(1, 8), Tooltip("Screen pixels per city pixel.")]
        private int pixelSize = 3;
        [SerializeField, Range(2, 32), Tooltip("Colour steps per channel.")]
        private int levels = 6;
        [SerializeField, Range(0f, 1f)] private float dither = 0.8f;

        private static readonly int LevelsId = Shader.PropertyToID("_Levels");
        private static readonly int DitherId = Shader.PropertyToID("_Dither");

        private UnityEngine.Camera _camera;
        private RenderTexture _raw;
        private RenderTexture _display;
        private RenderTexture _blurHalf;
        private RenderTexture _blur;
        private Material _quantize;
        private bool _flying;
        private Vector3 _centre;
        private float _radius;
        /// <summary>Smoothed pull-in from walls (0 = full orbit radius), so the camera does not jump.</summary>
        private float _pull;
        /// <summary>Smoothed lift over buildings under the camera (they have no colliders).</summary>
        private float _lift;
        private readonly System.Collections.Generic.List<Bounds> _buildings = new();
        /// <summary>Spacing of the sprawl copies: about the width of the core city.</summary>
        private float _span;
        /// <summary>The tallest core buildings: subjects for the tower shot.</summary>
        private readonly System.Collections.Generic.List<Bounds> _towers = new();

        /// <summary>The city is loaded and the camera flies (centre and radius are set).</summary>
        public bool Ready => _flying;
        /// <summary>The city centre on its ground (median building).</summary>
        public Vector3 Centre => _centre;
        /// <summary>The camera orbit radius, metres.</summary>
        public float Radius => _radius;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetBridge(PresentationBridge value) => bridge = value;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetScene(string value) => sceneName = value;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetFocusVolume(UnityEngine.Rendering.Volume value) => focusVolume = value;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetQuantizeShader(Shader value) => quantizeShader = value;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetSkirmish(HomeSkirmish value) => skirmish = value;

        private void OnDestroy()
        {
            if (_camera != null) _camera.targetTexture = null;
            Release(ref _raw);
            Release(ref _display);
            Release(ref _blurHalf);
            Release(ref _blur);
            if (_quantize != null) Destroy(_quantize);
        }

        private static void Release(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            Destroy(texture);
            texture = null;
        }

        /// <summary>
        /// (Re)makes the low-resolution camera target and the quantized display texture for the current screen size.
        /// Returns the texture the UI shows, or null for full resolution (no shader).
        /// </summary>
        private Texture EnsureTargets()
        {
            if (quantizeShader == null || !quantizeShader.isSupported) return null;
            var width = Mathf.Max(64, Screen.width / Mathf.Max(pixelSize, 1));
            var height = Mathf.Max(36, Screen.height / Mathf.Max(pixelSize, 1));
            if (_raw != null && _raw.width == width && _raw.height == height) return _display;

            Release(ref _raw);
            Release(ref _display);
            Release(ref _blurHalf);
            Release(ref _blur);
            // Glass behind the buttons: two bilinear halvings of the city image, shown stretched (a cheap soft blur).
            _blurHalf = new RenderTexture(Mathf.Max(8, width / 4), Mathf.Max(8, height / 4), 0, RenderTextureFormat.ARGB32)
                { name = "Home City (blur 1)", filterMode = FilterMode.Bilinear };
            _blur = new RenderTexture(Mathf.Max(4, width / 10), Mathf.Max(4, height / 10), 0, RenderTextureFormat.ARGB32)
                { name = "Home City (blur)", filterMode = FilterMode.Bilinear };
            _blurHalf.Create();
            _blur.Create();
            _raw = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "Home City (raw)", filterMode = FilterMode.Point };
            _display = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = "Home City", filterMode = FilterMode.Point };
            _raw.Create();
            _display.Create();
            if (_quantize == null) _quantize = new Material(quantizeShader) { hideFlags = HideFlags.HideAndDontSave };
            _camera.targetTexture = _raw;
            return _display;
        }

        private void Awake() => _camera = GetComponent<UnityEngine.Camera>();

        private void Start()
        {
            if (string.IsNullOrEmpty(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName)) return;
            var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (load != null) load.completed += _ => OnLoaded();
        }

        private void OnLoaded()
        {
            if (this == null) return;
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !FindCity(scene, out _centre, out _radius)) return;
            Sprawl(scene);

            // The city scene holds no cameras or listeners of its own; turn any off so this one renders.
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var other in root.GetComponentsInChildren<UnityEngine.Camera>(true)) other.enabled = false;
                foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = fogColour;
            RenderSettings.fogDensity = fogDensity;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = fogColour;
            _camera.fieldOfView = fieldOfView;
            _camera.farClipPlane = Mathf.Max(_camera.farClipPlane, _radius * 10f);
            _flying = true;
            Place(snap: true);

            var home = bridge != null ? bridge.Context.Home : null;
            home?.SetLiveBackdrop(true, EnsureTargets(), _blur);
        }

        private void LateUpdate()
        {
            if (_flying)
            {
                Place(snap: false);
                // Screen size changed: new targets, and the UI gets the new texture.
                var shown = _display;
                var display = EnsureTargets();
                if (display != shown) (bridge != null ? bridge.Context.Home : null)?.SetLiveBackdrop(true, display, _blur);
                // Last frame's camera image (one frame behind) through the quantizer, then the blur chain.
                if (_raw != null && _quantize != null)
                {
                    _quantize.SetFloat(LevelsId, levels);
                    _quantize.SetFloat(DitherId, dither);
                    Graphics.Blit(_raw, _display, _quantize);
                    Graphics.Blit(_raw, _blurHalf);
                    Graphics.Blit(_blurHalf, _blur);
                }
            }
            if (focusVolume == null) return;
            var home = bridge != null ? bridge.Context.Home : null;
            var target = home != null && home.Dimmed ? 1f : 0f;
            focusVolume.weight = Mathf.MoveTowards(focusVolume.weight, target, Time.unscaledDeltaTime / Mathf.Max(focusFadeSeconds, 0.01f));
        }

        // ---- Shot director: a spectator broadcast cut like an advert ----

        private enum Shot { Orbit, Chase, FlyBy, Track, Tower }

        private Shot _shot = Shot.Orbit;
        private float _shotEnd;
        private float _shotStart;
        private int _subject;
        private Vector3 _flyByPoint;
        private Bounds _tower;
        private Vector3 _towerSide;
        private Vector3 _smoothAim;

        /// <summary>Picks the next shot (never the same kind twice in a row); chopper shots need the skirmish.</summary>
        private void NextShot(float t)
        {
            var hasChoppers = skirmish != null && skirmish.FlyerCount > 0;
            Shot next;
            var guard = 0;
            do
            {
                var roll = Random.value;
                next = !hasChoppers ? (roll < 0.6f ? Shot.Orbit : Shot.Tower)
                    : roll < 0.18f ? Shot.Orbit : roll < 0.42f ? Shot.Chase : roll < 0.64f ? Shot.FlyBy : roll < 0.84f ? Shot.Track : Shot.Tower;
            } while (next == _shot && ++guard < 8);
            _shot = next;
            _shotStart = t;
            _shotEnd = t + Random.Range(shotSeconds.x, shotSeconds.y);
            if (hasChoppers) _subject = Random.Range(0, skirmish.FlyerCount);

            if (_shot == Shot.FlyBy && skirmish.TryGetFlyer(_subject, out var p, out var v))
            {
                // Wait ahead of the chopper, off to one side, a little below: it roars past.
                var forward = v.sqrMagnitude > 1f ? v.normalized : Vector3.forward;
                var side = Vector3.Cross(Vector3.up, forward).normalized * (Random.value < 0.5f ? 1f : -1f);
                _flyByPoint = p + v * Random.Range(1.6f, 2.4f) + side * Random.Range(30f, 60f) + Vector3.down * 12f;
            }
            if (_shot == Shot.Tower && _towers.Count > 0)
            {
                _tower = _towers[Random.Range(0, _towers.Count)];
                var angle = Random.value * Mathf.PI * 2f;
                _towerSide = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }
            (bridge != null ? bridge.Context.Home : null)?.NotifyCut();
        }

        private void Place(bool snap)
        {
            var t = Time.unscaledTime;
            var home = bridge != null ? bridge.Context.Home : null;
            var calm = home != null && home.Dimmed;
            if (calm && _shot != Shot.Orbit)
            {
                // A submenu is open: back to the slow orbit (no cuts behind text).
                _shot = Shot.Orbit;
                snap = true;
            }
            else if (!calm && t >= _shotEnd)
            {
                NextShot(t);
                snap = true;
            }

            var useLift = true;
            var usePull = false;
            var offset = screenOffset;
            var fov = fieldOfView;
            Vector3 wanted, aim;
            var p = Vector3.zero;
            var v = Vector3.zero;
            var flyer = skirmish != null && skirmish.TryGetFlyer(_subject, out p, out v);
            var forward = flyer && v.sqrMagnitude > 1f ? v.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var age = t - _shotStart;
            switch (_shot)
            {
                case Shot.Chase when flyer:
                    wanted = p - forward * 55f + Vector3.up * 16f + right * 8f;
                    aim = p + forward * 40f;
                    fov = 58f;
                    offset = 0.22f;
                    break;
                case Shot.Track when flyer:
                    wanted = p + right * 70f + Vector3.up * 6f + forward * (20f - age * 4f);
                    aim = p + forward * 12f;
                    fov = 40f;
                    offset = 0.25f;
                    break;
                case Shot.FlyBy when flyer:
                    wanted = _flyByPoint;
                    aim = p;
                    fov = 34f;
                    offset = 0.1f;
                    break;
                case Shot.Tower when _towers.Count > 0:
                    // Beside a tall tower, rising slowly, looking up its face.
                    var reach = Mathf.Max(_tower.extents.x, _tower.extents.z) + 90f;
                    wanted = new Vector3(_tower.center.x, _tower.min.y + 40f + age * 18f, _tower.center.z) + _towerSide * reach;
                    aim = new Vector3(_tower.center.x, Mathf.Min(_tower.max.y, wanted.y + 160f), _tower.center.z);
                    fov = 62f;
                    offset = 0.3f;
                    useLift = false;
                    break;
                default:
                    var angle = t / Mathf.Max(orbitSeconds, 1f) * Mathf.PI * 2f;
                    aim = _centre + Vector3.up * lookHeight;
                    wanted = _centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _radius + Vector3.up * (height + Mathf.Sin(t * 0.21f) * bob);
                    usePull = true;
                    break;
            }

            // A wall between the aim point and the camera: come in to just short of it.
            var pull = 0f;
            var toCamera = wanted - aim;
            if (usePull && Physics.SphereCast(aim, clearance * 0.5f, toCamera.normalized, out var hit, toCamera.magnitude, ~0, QueryTriggerInteraction.Ignore))
                pull = toCamera.magnitude - Mathf.Max(hit.distance - clearance, 0f);
            _pull = snap ? pull : Mathf.Lerp(_pull, pull, 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime));
            var position = wanted - toCamera.normalized * _pull;

            // Over a building: rise above its top (not for the tower shot, which stands beside one).
            var lift = 0f;
            if (useLift)
                foreach (var b in _buildings)
                {
                    if (position.x < b.min.x - clearance || position.x > b.max.x + clearance ||
                        position.z < b.min.z - clearance || position.z > b.max.z + clearance) continue;
                    lift = Mathf.Max(lift, b.max.y + clearance - position.y);
                }
            _lift = snap ? lift : Mathf.Lerp(_lift, lift, 1f - Mathf.Exp(-2f * Time.unscaledDeltaTime));
            position.y += _lift;

            // Stay above the ground under the camera.
            if (Physics.Raycast(position + Vector3.up * 2000f, Vector3.down, out var ground, 4000f, ~0, QueryTriggerInteraction.Ignore))
                position.y = Mathf.Max(position.y, ground.point.y + clearance);

            // Aim and lens ease a little inside a shot (a camera operator), but cut hard between shots.
            _smoothAim = snap ? aim : Vector3.Lerp(_smoothAim, aim, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            _camera.fieldOfView = snap ? fov : Mathf.Lerp(_camera.fieldOfView, fov, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));

            // Turn left a little so the subject sits to the right of the screen, clear of the menu.
            var halfWidth = Mathf.Atan(Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * _camera.aspect);
            var yaw = Mathf.Atan(Mathf.Tan(halfWidth) * offset) * Mathf.Rad2Deg;
            var look = _smoothAim - position;
            if (look.sqrMagnitude < 0.01f) look = transform.forward;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(look, Vector3.up) * Quaternion.Euler(0f, -yaw, 0f));
        }

        /// <summary>
        /// Centre: the median building position (x, z) on the median building base height. Radius: the distance from
        /// the centre that holds <see cref="radiusShare"/> of the buildings, × <see cref="radiusScale"/>.
        /// </summary>
        private bool FindCity(Scene scene, out Vector3 centre, out float radius)
        {
            centre = default;
            radius = 0f;
            var points = new System.Collections.Generic.List<Vector3>();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != focusRoot) continue;
                foreach (Transform building in root.transform)
                {
                    var renderers = building.GetComponentsInChildren<Renderer>();
                    if (renderers.Length == 0) continue;
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                    points.Add(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
                    _buildings.Add(bounds);
                }
            }
            if (points.Count == 0) return false;
            var tallest = new System.Collections.Generic.List<Bounds>(_buildings);
            tallest.Sort((a, b) => b.size.y.CompareTo(a.size.y));
            for (var i = 0; i < tallest.Count && i < 10; i++) _towers.Add(tallest[i]);

            centre = new Vector3(Median(points, p => p.x), Median(points, p => p.y), Median(points, p => p.z));
            var distances = new System.Collections.Generic.List<float>(points.Count);
            foreach (var p in points) distances.Add(Vector2.Distance(new Vector2(p.x, p.z), new Vector2(centre.x, centre.z)));
            distances.Sort();
            _span = distances[Mathf.Clamp(Mathf.RoundToInt(distances.Count * 0.9f), 0, distances.Count - 1)] * 2.2f;
            radius = distances[Mathf.Clamp(Mathf.RoundToInt(distances.Count * radiusShare), 0, distances.Count - 1)] * radiusScale;
            radius = Mathf.Max(radius, 100f);
            return true;
        }

        /// <summary>
        /// Copies of the buildings root on a grid round the core (ring 1 = 8 copies), each turned by a multiple of 90
        /// degrees about the core centre so the repeats do not read as copies.
        /// </summary>
        private void Sprawl(Scene scene)
        {
            if (sprawlRings <= 0 || _span <= 0f) return;
            GameObject core = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == focusRoot) core = root;
            if (core == null) return;
            var turn = 1;
            for (var x = -sprawlRings; x <= sprawlRings; x++)
            for (var z = -sprawlRings; z <= sprawlRings; z++)
            {
                if (x == 0 && z == 0) continue;
                var copy = Instantiate(core);
                copy.name = $"{focusRoot} Sprawl {x},{z}";
                SceneManager.MoveGameObjectToScene(copy, scene);
                copy.transform.RotateAround(_centre, Vector3.up, 90f * (turn++ % 4));
                copy.transform.position += new Vector3(x * _span, 0f, z * _span);
                // The camera rises over these too.
                foreach (Transform building in copy.transform)
                {
                    var renderers = building.GetComponentsInChildren<Renderer>();
                    if (renderers.Length == 0) continue;
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                    _buildings.Add(bounds);
                }
            }
        }
        private static float Median(System.Collections.Generic.List<Vector3> points, System.Func<Vector3, float> axis)
        {
            var values = new System.Collections.Generic.List<float>(points.Count);
            foreach (var p in points) values.Add(axis(p));
            values.Sort();
            return values[values.Count / 2];
        }
    }
}
