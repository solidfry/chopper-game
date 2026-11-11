using Chopper.Presentation.Bridge;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chopper.Presentation.Menu
{
    /// <summary>
    /// Home screen backdrop: loads a city scene (<see cref="sceneName"/>: HomeCity, a simplified City04 from Setup step 11) under the menu and flies
    /// this camera in a slow orbit round its buildings. The terminal look comes from the home post Volume (no colour,
    /// green tint, bloom, grain; Setup step 11) and dark green fog set here; the home UI lays a CRT layer over it
    /// (<see cref="HomeModel.LiveBackdrop"/>). Without the scene in the build the drawn grid stays. Put it on the
    /// home camera. The scene goes away with the home scene (single scene load).
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public sealed class HomeFlyover : MonoBehaviour
    {
        [SerializeField, Tooltip("Scene presentation bridge; it owns the home model.")]
        private PresentationBridge bridge;
        [SerializeField, Tooltip("City art scene (in the build settings). Empty = drawn grid only.")]
        private string sceneName = "HomeCity";
        [SerializeField, Tooltip("Root object whose renderers set the orbit centre and size.")]
        private string focusRoot = "Buildings";
        [SerializeField, Tooltip("Orbit radius as a share of the city's half width.")]
        private float radiusScale = 0.45f;
        [SerializeField, Tooltip("Camera height as a share of the tallest building.")]
        private float heightScale = 0.7f;
        [SerializeField, Tooltip("Seconds per full orbit.")]
        private float orbitSeconds = 240f;
        [SerializeField, Tooltip("Up and down drift, metres.")]
        private float bob = 25f;
        [SerializeField] private Color fogColour = new(0.01f, 0.05f, 0.03f);
        [SerializeField] private float fogDensity = 0.00045f;

        private UnityEngine.Camera _camera;
        private bool _flying;
        private Vector3 _centre;
        private float _radius;
        private float _height;
        private float _lookHeight;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetBridge(PresentationBridge value) => bridge = value;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetScene(string value) => sceneName = value;

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
            if (!scene.IsValid() || !FindBounds(scene, out var bounds)) return;

            // The city scene holds no cameras or listeners of its own; turn any off so this one renders.
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var other in root.GetComponentsInChildren<UnityEngine.Camera>(true)) other.enabled = false;
                foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            }

            _centre = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            _radius = Mathf.Max(bounds.extents.x, bounds.extents.z) * radiusScale;
            _height = bounds.min.y + bounds.size.y * heightScale;
            _lookHeight = bounds.min.y + bounds.size.y * 0.2f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = fogColour;
            RenderSettings.fogDensity = fogDensity;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = fogColour;
            _camera.farClipPlane = Mathf.Max(_camera.farClipPlane, _radius * 6f);
            _flying = true;
            Place();

            var home = bridge != null ? bridge.Context.Home : null;
            home?.SetLiveBackdrop(true);
        }

        private void LateUpdate()
        {
            if (_flying) Place();
        }

        private void Place()
        {
            var t = Time.unscaledTime;
            var angle = t / Mathf.Max(orbitSeconds, 1f) * Mathf.PI * 2f;
            var position = _centre + new Vector3(Mathf.Cos(angle) * _radius, 0f, Mathf.Sin(angle) * _radius);
            position.y = _height + Mathf.Sin(t * 0.21f) * bob;
            // Look a little ahead along the orbit, so the city slides past.
            var target = _centre + new Vector3(Mathf.Cos(angle + 0.9f) * _radius * 0.25f, 0f, Mathf.Sin(angle + 0.9f) * _radius * 0.25f);
            target.y = _lookHeight;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
        }

        private bool FindBounds(Scene scene, out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!string.IsNullOrEmpty(focusRoot) && root.name != focusRoot) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    if (!found) bounds = r.bounds;
                    else bounds.Encapsulate(r.bounds);
                    found = true;
                }
            }
            return found;
        }
    }
}
