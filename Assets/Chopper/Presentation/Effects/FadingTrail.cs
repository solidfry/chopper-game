using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Fades a whole trail out over time, not along its length (owner 27 Nov: the railgun trail should hang the full path
    /// and fade). Restarts each time the pooled round is spawned again.
    /// </summary>
    [RequireComponent(typeof(TrailRenderer))]
    public sealed class FadingTrail : MonoBehaviour
    {
        [SerializeField, Min(0.05f), Tooltip("Seconds from full to gone. Match the trail's Time.")]
        private float seconds = 1.5f;

        private TrailRenderer _trail;
        private Gradient _source;
        private Gradient _faded;
        private GradientAlphaKey[] _alphas;
        private float _age;

        public void Initialise(float fadeSeconds) => seconds = fadeSeconds;

        private void Awake()
        {
            _trail = GetComponent<TrailRenderer>();
            _source = _trail.colorGradient;
            _faded = new Gradient { mode = _source.mode };
            _alphas = new GradientAlphaKey[_source.alphaKeys.Length];
        }

        private void OnEnable() => _age = 0f;

        private void LateUpdate()
        {
            _age += Time.deltaTime;
            var left = 1f - Mathf.Clamp01(_age / seconds);
            left *= left;
            var keys = _source.alphaKeys;
            for (var i = 0; i < keys.Length; i++) _alphas[i] = new GradientAlphaKey(keys[i].alpha * left, keys[i].time);
            _faded.SetKeys(_source.colorKeys, _alphas);
            _trail.colorGradient = _faded;
        }
    }
}
