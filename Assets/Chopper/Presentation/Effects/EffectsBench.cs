using UnityEngine;
using UnityEngine.InputSystem;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// The effects bench (Setup window: "Open effects bench scene"): fires the game's own effects in a plain scene with
    /// no netcode, bots or HUD, through the same code the match uses (<see cref="CombatEffects"/>,
    /// <see cref="BlastSequence"/>). Its inspector has a button per effect; in play the keys work too:
    /// 1 ground blast, 2 air blast, 3 missile impact, 4 rocket impact, 5 gun impact, 6 next single layer,
    /// R repeat the last, C clear, [ and ] slower and faster time. Not a game scene.
    /// </summary>
    public sealed class EffectsBench : MonoBehaviour
    {
        [SerializeField] private CombatEffectsSettings settings;
        [SerializeField, Tooltip("Where blasts go off (the ground point).")] private Transform target;
        [SerializeField, Tooltip("Height of the air blast above the target, m.")] private float airHeight = 30f;
        [SerializeField, Tooltip("The wreck's speed at an air blast, m/s (the fall drifts this way).")]
        private Vector3 airVelocity = new(10f, 0f, 0f);
        [SerializeField, Range(0.05f, 2f), Tooltip("Time scale while the bench runs (slow motion to study a layer).")]
        private float timeScale = 1f;

        private CombatEffects _effects;
        private readonly BlastSequence _sequence = new();
        private System.Action _last;
        private int _nextLayer;

        public CombatEffectsSettings Settings => settings;
        public int LayerCount => settings != null ? settings.ChopperExplosionLayers.Count : 0;
        public float TimeScale { get => timeScale; set => timeScale = Mathf.Clamp(value, 0.05f, 2f); }
        private Vector3 Point => target != null ? target.position : transform.position;

        /// <summary>Editor only: the scene builder links the settings and the target.</summary>
        public void Configure(CombatEffectsSettings effectSettings, Transform blastPoint)
        {
            settings = effectSettings;
            target = blastPoint;
        }

        private void Awake() => _effects = new CombatEffects(settings, transform);

        private void OnDestroy()
        {
            _effects?.Dispose();
            Time.timeScale = 1f;
        }

        private void Update()
        {
            Time.timeScale = timeScale;
            Keys();
            _effects.Tick(Time.deltaTime);
            _sequence.Tick(_effects, Time.time);
        }

        public void GroundBlast() => Run(() => _sequence.Start(_effects, Point + Vector3.up * 1.5f, Vector3.zero, Time.time, Cast));
        public void AirBlast() => Run(() => _sequence.Start(_effects, Point + Vector3.up * airHeight, airVelocity, Time.time, Cast));
        public void MissileImpact() => Run(() => _effects.SpawnOneShot(settings.MissileImpact, Point + Vector3.up, Quaternion.identity));
        public void RocketImpact() => Run(() => _effects.SpawnOneShot(settings.RocketImpact, Point + Vector3.up, Quaternion.identity));
        public void GunImpact() => Run(() => _effects.SpawnOneShot(settings.GunImpact, Point + Vector3.up * 0.2f, Quaternion.LookRotation(Vector3.up)));

        /// <summary>One explosion layer alone at the ground point (its Up offset still applies).</summary>
        public void Layer(int index) => Run(() => _sequence.StartLayer(index, Point, Time.time));

        public void NextLayer()
        {
            if (LayerCount == 0) return;
            var index = _nextLayer % LayerCount;
            _nextLayer = index + 1;
            Layer(index);
        }

        /// <summary>Removes every live effect and waiting blast.</summary>
        public void Clear()
        {
            _sequence.Clear();
            _effects?.Dispose();
            _effects = new CombatEffects(settings, transform);
        }

        private void Run(System.Action action)
        {
            if (_effects == null || settings == null)
            {
                Debug.LogWarning("Effects bench: enter play mode first (and link the effects settings).");
                return;
            }
            _last = action;
            action();
        }

        private void Keys()
        {
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.digit1Key.wasPressedThisFrame) GroundBlast();
            if (keys.digit2Key.wasPressedThisFrame) AirBlast();
            if (keys.digit3Key.wasPressedThisFrame) MissileImpact();
            if (keys.digit4Key.wasPressedThisFrame) RocketImpact();
            if (keys.digit5Key.wasPressedThisFrame) GunImpact();
            if (keys.digit6Key.wasPressedThisFrame) NextLayer();
            if (keys.rKey.wasPressedThisFrame && _last != null) _last();
            if (keys.cKey.wasPressedThisFrame) Clear();
            if (keys.leftBracketKey.wasPressedThisFrame) TimeScale = timeScale * 0.5f;
            if (keys.rightBracketKey.wasPressedThisFrame) TimeScale = timeScale * 2f;
        }

        /// <summary>The bench's ground: scene colliders.</summary>
        private static bool Cast(Vector3 from, Vector3 to, out Vector3 point, out float fraction)
        {
            var delta = to - from;
            var length = delta.magnitude;
            if (length > 0f && Physics.Raycast(from, delta / length, out var hit, length, ~0, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                fraction = hit.distance / length;
                return true;
            }
            point = to;
            fraction = 1f;
            return false;
        }
    }
}
