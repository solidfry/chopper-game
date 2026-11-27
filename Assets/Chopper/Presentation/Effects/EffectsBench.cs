using UnityEngine;
using UnityEngine.InputSystem;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// The effects bench (Setup window: "Open effects bench scene"): fires the game's own effects in a plain scene with
    /// no netcode, bots or HUD, through the same code the match uses (<see cref="CombatEffects"/>,
    /// <see cref="BlastSequence"/>). Its inspector has a button per effect; in play the keys work too:
    /// 1 ground blast, 2 air blast, 3 missile impact, 4 rocket impact, 5 gun impact, 6 next single layer,
    /// 7 hit sparks, 8 crash dust, 9 shoot the chopper down (blast and debris), 0 flares, B boost, E engine, H damaged chopper health (100, 50, 25, 10 %), F fly a circle or hover,
    /// U hang the nuke under the chopper on its cable (as in Capture the Nuke), N nuke blast,
    /// R repeat the last, C clear, [ and ] slower and faster time. Not a game scene.
    /// </summary>
    public sealed class EffectsBench : MonoBehaviour
    {
        [SerializeField] private CombatEffectsSettings settings;
        [SerializeField, Tooltip("Lens dirt and sun flare, as on the game camera.")]
        private Chopper.Presentation.Camera.CameraSettings cameraSettings;
        [SerializeField, Tooltip("The game's sound library: the nuke blast sound (N), as in a match.")]
        private Chopper.Presentation.Audio.SoundLibrary sounds;
        [SerializeField, Tooltip("Where blasts go off (the ground point).")] private Transform target;
        [SerializeField, Tooltip("Height of the air blast above the target, m.")] private float airHeight = 30f;
        [SerializeField, Tooltip("The wreck's speed at an air blast, m/s (the fall drifts this way).")]
        private Vector3 airVelocity = new(10f, 0f, 0f);
        [SerializeField, Range(0.05f, 2f), Tooltip("Time scale while the bench runs (slow motion to study a layer).")]
        private float timeScale = 1f;

        [Header("Damaged chopper")]
        [SerializeField, Tooltip("A stand-in chopper body (moved by the bench). Its smoke and fire follow its health.")]
        private Transform standIn;
        [SerializeField, Range(0f, 1f), Tooltip("Health share: smoke below the settings' Smoke Below, fire below Fire Below.")]
        private float health = 1f;
        [SerializeField, Tooltip("Height of the stand-in above the target, m.")] private float hoverHeight = 10f;
        [SerializeField, Tooltip("Circle radius, m (0 = hover in place): flying shows how the smoke trails.")]
        private float circleRadius;
        [SerializeField, Tooltip("Speed round the circle, m/s.")] private float circleSpeed = 25f;
        [SerializeField, Tooltip("Engine modules to try on the stand-in (E changes): their model and nozzles, as in a match.")]
        private Chopper.Simulation.Loadout.ModuleDefinition[] engines = System.Array.Empty<Chopper.Simulation.Loadout.ModuleDefinition>();
        [SerializeField, Tooltip("A nuke model (U hangs it under the stand-in on the match's cable). Hidden until then.")]
        private Transform nukeStandIn;

        /// <summary>The engine module's two exhausts (as the game's ExhaustPoint) and one hit on the tail boom.</summary>
        private static readonly (Vector3 Point, Vector3 Normal)[] HullPoints =
        {
            (new Vector3(0.28f, 1.1f, -2.35f), Vector3.up),
            (new Vector3(-0.28f, 1.1f, -2.35f), Vector3.up),
            (new Vector3(-0.3f, 1.2f, -4f), new Vector3(-1f, 0.3f, 0f)),
        };

        private CombatEffects _effects;
        private DamageParticles _damage;
        private readonly BlastSequence _sequence = new();
        private readonly NukeBlastPlayer _nuke = new();
        private readonly Chopper.Presentation.Audio.NukeBlastSound _nukeSound = new();
        private Chopper.Presentation.Audio.AudioService _audio;
        private float _smokeCarry;
        private float _flameCarry;
        private float _angle;
        private Vector3 _lastStandIn;
        private bool _hasLast;
        private Vector3 _lastVelocity;
        private float _hiddenUntil;
        private readonly FlareVolleys<int> _flares = new();
        private readonly BoostFlames<int> _boost = new();
        private readonly System.Collections.Generic.List<(Vector3 Position, Vector3 Direction, float Size)> _exhausts = new();
        private int _engine;
        private float _boostUntil;
        /// <summary>The gunship's engine socket (SOCKET_Engine_0): the exhaust points back.</summary>
        private static readonly Vector3 EngineSocket = new(0f, 1.1f, -1.85f);
        private System.Action _last;
        private int _nextLayer;

        public CombatEffectsSettings Settings => settings;
        public int LayerCount => settings != null ? settings.ChopperExplosionLayers.Count : 0;
        public float TimeScale { get => timeScale; set => timeScale = Mathf.Clamp(value, 0.05f, 2f); }
        private Vector3 Point => target != null ? target.position : transform.position;

        public float Health { get => health; set => health = Mathf.Clamp01(value); }
        public float CircleRadius { get => circleRadius; set => circleRadius = Mathf.Max(0f, value); }
        private Vector3 ChopperPoint => standIn != null ? standIn.position : Point + Vector3.up * hoverHeight;

        /// <summary>Editor only: the scene builder links the settings, the target and the stand-in chopper.</summary>
        public void Configure(CombatEffectsSettings effectSettings, Transform blastPoint, Transform chopperStandIn = null,
            Chopper.Simulation.Loadout.ModuleDefinition[] engineModules = null, Chopper.Presentation.Camera.CameraSettings lens = null,
            Chopper.Presentation.Audio.SoundLibrary soundLibrary = null, Transform nuke = null)
        {
            nukeStandIn = nuke;
            sounds = soundLibrary;
            cameraSettings = lens;
            engines = engineModules ?? System.Array.Empty<Chopper.Simulation.Loadout.ModuleDefinition>();
            settings = effectSettings;
            target = blastPoint;
            standIn = chopperStandIn;
        }

        private Chopper.Presentation.Camera.LensLook _lens;

        private void Awake()
        {
            Create();
            _nuke.Prewarm(settings);
            _audio = new Chopper.Presentation.Audio.AudioService(sounds, transform);
            // Open ground, as on the training range.
            _audio.SetSpace(sounds != null ? sounds.Space(false) : null);
            _lens = new Chopper.Presentation.Camera.LensLook(cameraSettings, UnityEngine.Camera.main);
        }

        private void OnDestroy()
        {
            _effects?.Dispose();
            _damage?.Dispose();
            _boost.Clear();
            _nuke.Clear();
            _nukeSound.Clear();
            _audio?.Dispose();
            _lens?.Dispose();
            _rope?.Dispose();
            Time.timeScale = 1f;
        }

        private NukeRopeView _rope;
        private bool _carrying;

        /// <summary>U: the stand-in carries the nuke (or lets it go). F flies the circle so the cable bows back.</summary>
        public void ToggleCarry()
        {
            if (settings == null || settings.NukeRopeMaterial == null)
            {
                Debug.LogWarning("Effects bench: no nuke rope material in the effects settings (run Setup).");
                return;
            }
            _carrying = !_carrying;
            if (nukeStandIn != null) nukeStandIn.gameObject.SetActive(_carrying);
            if (!_carrying) _rope?.Hide();
        }

        /// <summary>As the server: the nuke's hook eye <see cref="Chopper.Simulation.Match.NukeZones.HangBelow"/> under the
        /// chopper; the cable from the settings' winch point to it.</summary>
        private void Carry(float deltaTime)
        {
            if (!_carrying || standIn == null || !standIn.gameObject.activeInHierarchy || settings == null) return;
            var eye = standIn.position + Vector3.down * Chopper.Simulation.Match.NukeZones.HangBelow;
            if (nukeStandIn != null) nukeStandIn.SetPositionAndRotation(eye, Quaternion.Euler(0f, standIn.eulerAngles.y, 0f));
            _rope ??= new NukeRopeView(settings);
            _rope.Draw(settings, standIn.TransformPoint(settings.NukeRopeWinch), eye, 1f, deltaTime);
        }

        private void Update()
        {
            Time.timeScale = timeScale;
            Keys();
            _effects.Tick(Time.deltaTime);
            _sequence.Tick(_effects, Time.time);
            _nuke.Tick(settings, Time.time);
            _audio.Tick(Time.deltaTime);
            _nukeSound.Tick(_audio, sounds, Listener, Time.time);
            _effects.Debris.Tick(Time.deltaTime, Time.time, Ray);
            _flares.Tick(_effects, Time.deltaTime, StandInPose);
            TickGunBurst(Time.deltaTime);
            BoostFlame(Time.deltaTime);
            Damaged(Time.deltaTime);
            Carry(Time.deltaTime);
            if (standIn != null && _hiddenUntil > 0f && Time.time > _hiddenUntil)
            {
                standIn.gameObject.SetActive(true);
                _hiddenUntil = 0f;
            }
        }

        /// <summary>The stand-in boosts for 2.5 s: the kick burst, the flame and the trail (best seen flying a circle).</summary>
        public void Boost() => Run(() => _boostUntil = Time.time + 2.5f);

        private void BoostFlame(float deltaTime)
        {
            if (!BoostFlames<int>.Enabled(settings)) return;
            _boost.Begin();
            if (standIn != null && standIn.gameObject.activeSelf)
            {
                _exhausts.Clear();
                foreach (var (point, size) in Nozzles())
                    _exhausts.Add((standIn.TransformPoint(point), -standIn.forward, size));
                _boost.Update(settings, 0, Time.time < _boostUntil, _exhausts, 1f, deltaTime);
            }
            _boost.End(settings);
        }

        /// <summary>The fitted engine's nozzle mouths in stand-in space (the standard twin nozzles when no engine is set).</summary>
        private System.Collections.Generic.IEnumerable<(Vector3 Point, float Size)> Nozzles()
        {
            var engine = engines.Length > 0 ? engines[_engine % engines.Length] : null;
            if (engine == null || engine.Exhausts.Count == 0)
            {
                yield return (EngineSocket + new Vector3(0.28f, 0f, -0.45f), 1f);
                yield return (EngineSocket + new Vector3(-0.28f, 0f, -0.45f), 1f);
                yield break;
            }
            foreach (var nozzle in engine.Exhausts) yield return (EngineSocket + nozzle.Position, nozzle.Size > 0f ? nozzle.Size : 1f);
        }

        /// <summary>The next engine module: its model goes on the stand-in's engine socket.</summary>
        public void NextEngine() => Run(() =>
        {
            if (engines.Length == 0 || standIn == null) return;
            _engine = (_engine + 1) % engines.Length;
            var engine = engines[_engine];
            if (engine == null || engine.Visual == null) return;
            foreach (Transform child in standIn)
                if (child.name.StartsWith("Engine_")) Destroy(child.gameObject);
            var visual = Instantiate(engine.Visual, standIn);
            visual.name = "Engine_" + engine.DisplayName;
            visual.transform.localPosition = EngineSocket;
            visual.transform.localRotation = Quaternion.identity;
        });

        public string EngineName => engines.Length > 0 && engines[_engine % engines.Length] != null ? engines[_engine % engines.Length].DisplayName : "-";

        /// <summary>The stand-in drops a flare salvo (the settings' count, burn time and interval), as in a match.</summary>
        public void Flares() => Run(() => _flares.Drop(0, settings.FlaresPerDrop, settings.FlareLifetime));

        private bool StandInPose(int key, out Matrix4x4 pose, out Vector3 velocity)
        {
            pose = standIn != null ? standIn.localToWorldMatrix : Matrix4x4.Translate(ChopperPoint);
            velocity = _lastVelocity;
            return standIn == null || standIn.gameObject.activeSelf;
        }

        /// <summary>The stand-in is shot down in the air: the blast (as a match) and its own model breaks into debris.</summary>
        public void Wreck() => Run(() =>
        {
            if (standIn == null || !standIn.gameObject.activeSelf) return;
            var position = standIn.position;
            var velocity = _lastVelocity;
            // A stand-in entity: the debris only uses it as a key.
            _effects.Debris.TrackVisuals(new Unity.Entities.Entity { Index = 1, Version = 1 }, Time.time, new[] { standIn.gameObject });
            _effects.Debris.Burst(position, velocity);
            StartBlast(position, velocity);
            standIn.gameObject.SetActive(false);
            _hiddenUntil = Time.time + 6f;
        });

        private static bool Ray(Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal)
        {
            var delta = to - from;
            var length = delta.magnitude;
            if (length > 0f && Physics.Raycast(from, delta / length, out var hit, length, ~0, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }
            point = to;
            normal = Vector3.up;
            return false;
        }

        private void Create()
        {
            _effects = new CombatEffects(settings, transform);
            _damage = new DamageParticles(settings, transform);
            _effects.Debris.SetTrails(_damage.DebrisSmoke, _damage.DebrisFlames, _damage.DebrisSparks);
        }

        /// <summary>The stand-in flies its circle (or hovers) and smokes and burns for its health, as in a match.</summary>
        private void Damaged(float deltaTime)
        {
            if (settings == null || deltaTime <= 0f) return;
            var centre = Point + Vector3.up * hoverHeight;
            Vector3 position;
            Vector3 forward;
            if (circleRadius > 0.5f)
            {
                _angle += circleSpeed / circleRadius * deltaTime;
                position = centre + new Vector3(Mathf.Cos(_angle), 0f, Mathf.Sin(_angle)) * circleRadius;
                forward = new Vector3(-Mathf.Sin(_angle), 0f, Mathf.Cos(_angle));
            }
            else
            {
                position = centre;
                forward = standIn != null ? standIn.forward : Vector3.forward;
            }
            if (standIn != null) standIn.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            var velocity = _hasLast ? (position - _lastStandIn) / deltaTime : Vector3.zero;
            _lastVelocity = velocity;
            if (standIn != null && !standIn.gameObject.activeSelf) return;
            _lastStandIn = position;
            _hasLast = true;

            var smoke = DamageLook.Smoke(health, settings.SmokeBelow);
            var fire = DamageLook.Fire(health, settings.FireBelow);
            var colour = DamageLook.SmokeColour(health, settings.SmokeBelow, settings.HeavySmokeBelow, settings.LightSmokeColour,
                settings.HeavySmokeColour);
            _smokeCarry += settings.SmokeRate * smoke * deltaTime;
            _flameCarry += DamageEffectsSystem.FlameRate * fire * deltaTime;
            var smokeCount = (int)_smokeCarry;
            var flameCount = (int)_flameCarry;
            _smokeCarry -= smokeCount;
            _flameCarry -= flameCount;
            // Hit points on the hull (as in a match, where the damage comes from the hits): engine top and tail boom.
            var pose = standIn != null ? standIn.localToWorldMatrix : Matrix4x4.Translate(position);
            foreach (var (local, normal) in HullPoints)
            {
                var point = pose.MultiplyPoint3x4(local);
                var outward = pose.MultiplyVector(normal).normalized;
                _damage.EmitSmoke(point, outward, velocity, colour, smoke, smokeCount);
                _damage.EmitFlames(point, outward, velocity, fire, flameCount);
            }
        }

        /// <summary>A hit on the stand-in: sparks as for a heavy bullet hit.</summary>
        public void HitSparks() => Run(() => _damage.EmitSparks(ChopperPoint + Vector3.up, (Vector3.up + Random.insideUnitSphere).normalized,
            Mathf.Max(settings.SparkCount.x, settings.SparkCount.y)));

        /// <summary>Crash dust at the ground point (the bench uses the downwash colour; maps use their fog colour).</summary>
        public void CrashDust() => Run(() => _damage.EmitDust(Point, settings.DownwashColour, 40));

        public void GroundBlast() => Run(() => StartBlast(Point + Vector3.up * 1.5f, Vector3.zero));
        public void AirBlast() => Run(() => StartBlast(Point + Vector3.up * airHeight, airVelocity));

        /// <summary>A wreck blast as in a match: the blast cue, with the effect prefabs silent.</summary>
        private void StartBlast(Vector3 position, Vector3 velocity)
        {
            var cue = sounds != null && _audio != null ? sounds.WreckBlast : null;
            _sequence.Start(_effects, position, velocity, Time.time, Cast, muteAudio: cue != null);
            if (cue != null) _audio.Play(cue, position);
        }
        /// <summary>The Capture the Nuke blast (same player as the game), bursting <see cref="NukeBurstHeight"/> m up.
        /// F5 is the far view for it.</summary>
        public void Nuke() => Run(() =>
        {
            var burst = Point + Vector3.up * NukeBurstHeight;
            _nuke.Start(settings, burst, Point, UnityEngine.Camera.main, Time.time);
            _nukeSound.Add(burst, Listener, settings.NukeShockSpeed, Time.time);
        });

        private static Vector3 Listener => UnityEngine.Camera.main != null ? UnityEngine.Camera.main.transform.position : Vector3.zero;

        private const float NukeBurstHeight = 35f;

        private sealed class BenchRound
        {
            public GameObject Visual;
            public Vector3 Position;
            public Vector3 Direction;
            public float DistanceLeft;
            public WeaponLook Look;
        }

        private const int BurstRounds = 8;
        private const float BurstInterval = 0.09f;
        private const float BenchRoundSpeed = 250f;
        private readonly System.Collections.Generic.List<BenchRound> _rounds = new();
        private int _gunFamily = -1;
        private int _burstLeft;
        private float _burstTimer;
        private WeaponLook _burstLook;

        /// <summary>Name of the gun family the last G burst used (bench UI).</summary>
        public string GunFamilyName { get; private set; } = "";

        /// <summary>G: a burst of the next gun family (Docs/WeaponFeelPlan.md step 2) from the stand-in to the target:
        /// its tracer, flash, impact and fire sound.</summary>
        public void GunBurst() => Run(() =>
        {
            var library = settings.WeaponLooks;
            var families = library != null ? library.Families : null;
            if (families == null || families.Count == 0)
            {
                Debug.LogWarning("Effects bench: no weapon look families (press Setup '1. Create / repair config').");
                return;
            }
            _gunFamily = (_gunFamily + 1) % families.Count;
            _burstLook = families[_gunFamily].Look;
            GunFamilyName = families[_gunFamily].Name;
            _burstLeft = BurstRounds;
            _burstTimer = 0f;
            Debug.Log($"Effects bench: gun family {GunFamilyName}.");
        });

        private void TickGunBurst(float deltaTime)
        {
            if (_burstLeft > 0 && (_burstTimer -= deltaTime) <= 0f)
            {
                _burstTimer = BurstInterval;
                _burstLeft--;
                var muzzle = ChopperPoint + Vector3.down * 0.6f;
                var aim = Point + Vector3.up * 0.2f + Random.insideUnitSphere * 0.6f;
                var direction = (aim - muzzle).normalized;
                var rotation = Quaternion.LookRotation(direction);
                _effects.SpawnOneShot(settings.GunMuzzleFlashFor(_burstLook), muzzle, rotation);
                var cue = _burstLook != null && _burstLook.FireCue != null ? _burstLook.FireCue : sounds != null ? sounds.GunFire : null;
                if (cue != null) _audio.Play(cue, muzzle);
                _rounds.Add(new BenchRound
                {
                    Visual = _effects.SpawnPooled(settings.GunBulletFor(_burstLook), muzzle, rotation, muteAudio: !settings.GunBulletAudio),
                    Position = muzzle, Direction = direction, DistanceLeft = Vector3.Distance(muzzle, aim), Look = _burstLook,
                });
            }

            for (var i = _rounds.Count - 1; i >= 0; i--)
            {
                var round = _rounds[i];
                var move = Mathf.Min(BenchRoundSpeed * deltaTime, round.DistanceLeft);
                round.Position += round.Direction * move;
                round.DistanceLeft -= move;
                if (round.DistanceLeft > 0f)
                {
                    if (round.Visual != null) round.Visual.transform.position = round.Position;
                    continue;
                }
                _effects.SpawnOneShot(settings.GunImpactFor(round.Look), round.Position, Quaternion.LookRotation(-round.Direction));
                if (round.Visual != null) _effects.Release(round.Visual);
                _rounds.RemoveAt(i);
            }
        }

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
            _nuke.Clear();
            _nukeSound.Clear();
            _flares.Clear();
            _boost.Clear();
            _rounds.Clear();
            _burstLeft = 0;
            _effects?.Dispose();
            _damage?.Dispose();
            Create();
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
            if (keys.digit7Key.wasPressedThisFrame) HitSparks();
            if (keys.digit8Key.wasPressedThisFrame) CrashDust();
            if (keys.digit9Key.wasPressedThisFrame) Wreck();
            if (keys.digit0Key.wasPressedThisFrame) Flares();
            if (keys.nKey.wasPressedThisFrame) Nuke();
            if (keys.uKey.wasPressedThisFrame) ToggleCarry();
            if (keys.bKey.wasPressedThisFrame) Boost();
            if (keys.gKey.wasPressedThisFrame) GunBurst();
            if (keys.eKey.wasPressedThisFrame) NextEngine();
            if (keys.hKey.wasPressedThisFrame) Health = health > 0.75f ? 0.5f : health > 0.4f ? 0.25f : health > 0.15f ? 0.1f : 1f;
            if (keys.fKey.wasPressedThisFrame) CircleRadius = circleRadius > 0.5f ? 0f : 30f;
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
