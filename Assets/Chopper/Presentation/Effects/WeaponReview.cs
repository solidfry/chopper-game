using System.Collections.Generic;
using Chopper.Presentation.Audio;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Definitions;
using Chopper.Simulation.Loadout;
using Chopper.Simulation.Visuals;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Weapon review scene (owner, 27 Nov; Docs/WeaponFeelPlan.md): every weapon module in the catalog, fired from a
    /// stand-in chopper at a target with its real numbers (rate, round speed, gravity, drag, lifetime, salvo, cluster
    /// split) and its family look and sounds (<see cref="WeaponLookLibrary"/>). Up / Down pick, Space fires (hold for
    /// guns), X detonates what is in flight, [ ] slow / speed time. No netcode: a review tool, not a game scene.
    /// </summary>
    public sealed class WeaponReview : MonoBehaviour
    {
        [SerializeField] private CombatEffectsSettings settings;
        [SerializeField] private SoundLibrary sounds;
        [SerializeField] private LoadoutCatalog catalog;
        [SerializeField, Tooltip("Fires from here, along its forward axis.")] private Transform shooter;
        [SerializeField, Tooltip("What the weapons aim at.")] private Transform target;
        [SerializeField, Range(0.05f, 1f)] private float timeScale = 1f;
        [SerializeField, Tooltip("Its cockpit model and eye give the cockpit view.")] private ChassisDefinition chassis;
        [SerializeField, Tooltip("Start in the cockpit (C switches to the orbit camera).")] private bool cockpitView = true;
        [SerializeField, Tooltip("Degrees of shake at full trauma (cockpit view).")] private float shakeDegrees = 4f;
        [SerializeField, Tooltip("Trauma lost per second.")] private float shakeDecay = 1.5f;

        private sealed class Entry
        {
            public ModuleDefinition Module;
            public WeaponDefinition Weapon;
            public WeaponLook Look;
            public string Family;
        }

        private sealed class Round
        {
            public GameObject Visual;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Gravity, Drag, Remaining, SplashRadius;
            public WeaponLook Look;
            public GameObject Impact;
            public SoundCue Blast;
            public SoundLoop Loop;
            /// <summary>Guided: steers at the target at this speed.</summary>
            public float HomingSpeed;
            /// <summary>Cluster carrier: seconds to the split (below 0 = none).</summary>
            public float SplitIn = -1f;
            public ClusterConfig Cluster;
            /// <summary>Gun round: straight, ends at <see cref="Remaining"/> distance.</summary>
            public bool Bullet;
            /// <summary>Spawned this frame: not moved yet.</summary>
            public bool Fresh = true;
        }

        private readonly List<Entry> _entries = new();
        private readonly List<Round> _rounds = new();
        private CombatEffects _effects;
        private AudioService _audio;
        private int _selected;
        private float _gunCooldown;
        private float _lastFlash;
        private int _salvoLeft;
        private float _salvoTimer;
        private uint _seed = 1;
        private GUIStyle _style;
        private GameObject _cockpit;
        private Renderer[] _body = System.Array.Empty<Renderer>();
        private float _trauma;
        private float _orbitNear = 0.3f;

        /// <summary>Own gun from the cockpit, as in a match (<c>GunEffectsSystem.CockpitBulletStart</c>): no muzzle
        /// flash, rounds start this far ahead.</summary>
        private const float CockpitBulletStart = 8f;

        /// <summary>Editor only: the scene builder links the config parts and the stage.</summary>
        public void Configure(CombatEffectsSettings effectSettings, SoundLibrary soundLibrary, LoadoutCatalog loadout, Transform from,
            Transform at, ChassisDefinition cockpitChassis)
        {
            chassis = cockpitChassis;
            settings = effectSettings;
            sounds = soundLibrary;
            catalog = loadout;
            shooter = from;
            target = at;
        }

        private void Awake()
        {
            _effects = new CombatEffects(settings, transform);
            _audio = new AudioService(sounds, transform);
            _audio.SetSpace(sounds != null ? sounds.Space(false) : null);
            BuildEntries();
            if (shooter != null)
            {
                _body = shooter.GetComponentsInChildren<Renderer>(true);
                if (chassis != null && chassis.Cockpit != null)
                {
                    _cockpit = Instantiate(chassis.Cockpit, shooter, false);
                    _cockpit.name = "Cockpit";
                }
            }
            if (UnityEngine.Camera.main != null) _orbitNear = UnityEngine.Camera.main.nearClipPlane;
        }

        /// <summary>Cockpit view: the camera at the chassis eye, looking along the chopper, shaken by the gun kick.
        /// The body is hidden and the cockpit model shown, as for your own chopper in a match.</summary>
        private void LateUpdate()
        {
            var camera = UnityEngine.Camera.main;
            if (camera == null || shooter == null) return;
            var orbit = camera.GetComponent<BenchOrbitCamera>();
            if (orbit != null) orbit.enabled = !cockpitView;
            foreach (var body in _body) if (body != null) body.enabled = !cockpitView;
            if (_cockpit != null) _cockpit.SetActive(cockpitView);
            _trauma = Mathf.Max(0f, _trauma - shakeDecay * Time.deltaTime);
            if (!cockpitView)
            {
                camera.nearClipPlane = _orbitNear;
                return;
            }
            camera.nearClipPlane = 0.05f;
            var eye = chassis != null ? chassis.CockpitEye : new Vector3(0f, 1f, 1.15f);
            var shake = _trauma * _trauma * shakeDegrees;
            var t = Time.time * 25f;
            var wobble = Quaternion.Euler((Mathf.PerlinNoise(t, 0.1f) - 0.5f) * 2f * shake, (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * shake,
                (Mathf.PerlinNoise(t, 3.3f) - 0.5f) * shake);
            camera.transform.SetPositionAndRotation(shooter.TransformPoint(eye), shooter.rotation * wobble);
        }

        private void OnDestroy()
        {
            _effects?.Dispose();
            _audio?.Dispose();
            Time.timeScale = 1f;
        }

        /// <summary>Guns first, then rockets / bombs / mines, then guided; each in id order.</summary>
        private void BuildEntries()
        {
            _entries.Clear();
            if (catalog == null || settings == null) return;
            foreach (var module in catalog.Modules)
            {
                if (module == null) continue;
                var kind = module.Weapon switch
                {
                    HitscanWeaponDefinition => WeaponKind.Gun,
                    RocketWeaponDefinition r => WeaponLookLibrary.RocketKind(r.Gravity, r.Drag),
                    GuidedMissileDefinition => WeaponKind.Guided,
                    _ => (WeaponKind)255,
                };
                if ((byte)kind == 255) continue;
                _entries.Add(new Entry { Module = module, Weapon = module.Weapon, Look = settings.Look(module.Id, kind), Family = FamilyOf(module) });
            }
            _entries.Sort((a, b) => Order(a).CompareTo(Order(b)));
        }

        private static int Order(Entry entry) =>
            (entry.Weapon is HitscanWeaponDefinition ? 0 : entry.Weapon is RocketWeaponDefinition ? 1 : 2) * 100000 + entry.Module.Id;

        private string FamilyOf(ModuleDefinition module)
        {
            var library = settings.WeaponLooks;
            if (library != null)
                foreach (var family in library.Families)
                    if (family != null)
                        foreach (var member in family.Modules)
                            if (member == module) return family.Name;
            return "kind default";
        }

        private Entry Selected => _entries.Count > 0 ? _entries[Mathf.Clamp(_selected, 0, _entries.Count - 1)] : null;

        private void Update()
        {
            Time.timeScale = timeScale;
            var dt = Time.deltaTime;
            Keys(dt);
            TickSalvo(dt);
            MoveRounds(dt);
            _effects.Tick(dt);
            _audio.Tick(dt);
        }

        private void Keys(float dt)
        {
            var keys = Keyboard.current;
            if (keys == null || _entries.Count == 0) return;
            if (keys.downArrowKey.wasPressedThisFrame) _selected = (_selected + 1) % _entries.Count;
            if (keys.upArrowKey.wasPressedThisFrame) _selected = (_selected + _entries.Count - 1) % _entries.Count;
            if (keys.xKey.wasPressedThisFrame) DetonateAll();
            if (keys.cKey.wasPressedThisFrame) cockpitView = !cockpitView;
            if (keys.leftBracketKey.wasPressedThisFrame) timeScale = Mathf.Max(0.05f, timeScale * 0.5f);
            if (keys.rightBracketKey.wasPressedThisFrame) timeScale = Mathf.Min(1f, timeScale * 2f);

            var entry = Selected;
            _gunCooldown -= dt;
            if (entry.Weapon is HitscanWeaponDefinition gun)
            {
                // Hold to fire at the gun's real rate.
                if (keys.spaceKey.isPressed && _gunCooldown <= 0f)
                {
                    FireGun(entry, gun);
                    _gunCooldown = gun.FireInterval;
                }
            }
            else if (keys.spaceKey.wasPressedThisFrame) FirePylon(entry);
        }

        // ---- Guns ----

        private Vector3 Muzzle => shooter.position + shooter.forward * 3f - shooter.up * 0.6f;

        private Vector3 AimFrom(Vector3 muzzle, float spread) =>
            ((target.position + UnityEngine.Random.insideUnitSphere * spread) - muzzle).normalized;

        private void FireGun(Entry entry, HitscanWeaponDefinition gun)
        {
            var muzzle = Muzzle;
            var direction = AimFrom(muzzle, 1.2f);
            var rotation = Quaternion.LookRotation(direction);
            var distance = Vector3.Distance(muzzle, target.position);
            if (cockpitView)
            {
                // As in a match: the family's kick on your own gun; rounds start ahead of the nose, no flash.
                if (entry.Look != null) _trauma = Mathf.Min(1f, _trauma + entry.Look.CameraKick);
                var start = Mathf.Min(CockpitBulletStart, distance);
                muzzle += direction * start;
                distance -= start;
            }
            else if (Time.time - _lastFlash >= settings.GunMuzzleFlashInterval)
            {
                _effects.SpawnOneShot(settings.GunMuzzleFlashFor(entry.Look), muzzle, rotation);
                _lastFlash = Time.time;
            }
            var cue = entry.Look != null && entry.Look.FireCue != null ? entry.Look.FireCue : sounds != null ? sounds.GunFire : null;
            if (cue != null) _audio.Play(cue, muzzle);
            if (entry.Look != null && entry.Look.TailCue != null) _audio.Play(entry.Look.TailCue, muzzle);
            var speed = gun.BulletSpeed > 0f ? gun.BulletSpeed : settings.GunBulletSpeed;
            _rounds.Add(new Round
            {
                Visual = _effects.SpawnTracer(settings.GunBulletFor(entry.Look), muzzle, rotation, muteAudio: !settings.GunBulletAudio,
                    scale: CombatEffectsSettings.TracerScaleFor(entry.Look),
                    brightness: CombatEffectsSettings.TracerBrightnessFor(entry.Look)),
                Position = muzzle, Velocity = direction * speed, Remaining = distance, Look = entry.Look, Bullet = true,
                Impact = settings.GunImpactFor(entry.Look), Blast = sounds != null ? sounds.SurfaceHit(SurfaceKind.Concrete) : null,
            });
        }

        // ---- Pylons ----

        private void FirePylon(Entry entry)
        {
            _salvoLeft = entry.Weapon switch
            {
                RocketWeaponDefinition r => r.SalvoCount,
                GuidedMissileDefinition m => m.SalvoCount,
                _ => 1,
            };
            _salvoTimer = 0f;
        }

        private void TickSalvo(float dt)
        {
            if (_salvoLeft <= 0) return;
            _salvoTimer -= dt;
            if (_salvoTimer > 0f) return;
            var entry = Selected;
            _salvoLeft--;
            switch (entry.Weapon)
            {
                case RocketWeaponDefinition rocket:
                    LaunchRocket(entry, rocket);
                    _salvoTimer = rocket.SalvoInterval;
                    break;
                case GuidedMissileDefinition missile:
                    LaunchMissile(entry, missile);
                    _salvoTimer = Mathf.Max(missile.SalvoInterval, 0.05f);
                    break;
                default:
                    _salvoLeft = 0;
                    break;
            }
        }

        private void LaunchRocket(Entry entry, RocketWeaponDefinition rocket)
        {
            var muzzle = Muzzle;
            // Bombs and mines drop forward from the pylon; rockets fly at the target (with the salvo's spread).
            var dropped = rocket.Gravity > 0f || rocket.Drag > 0f;
            var direction = dropped ? shooter.forward : AimFrom(muzzle, rocket.SalvoCount > 1 ? 3f : 0.5f);
            var rotation = Quaternion.LookRotation(direction);
            _effects.SpawnOneShot(settings.RocketMuzzleFlashFor(entry.Look), muzzle, rotation);
            if (entry.Look != null && entry.Look.FireCue != null) _audio.Play(entry.Look.FireCue, muzzle);
            var visual = _effects.SpawnPooled(settings.RocketFor(entry.Look), muzzle, rotation);
            var scale = entry.Look == null || entry.Look.ScaleWithBlast ? RocketVisualSystem.RocketScale(rocket.SplashRadius) : 1f;
            if (visual != null) visual.transform.localScale = Vector3.one * scale;
            _rounds.Add(new Round
            {
                Visual = visual, Position = muzzle, Velocity = direction * rocket.Speed, Gravity = rocket.Gravity, Drag = rocket.Drag,
                Remaining = rocket.Lifetime, SplashRadius = rocket.SplashRadius, Look = entry.Look,
                Impact = settings.RocketImpactFor(entry.Look), Blast = sounds != null ? sounds.RocketBlast : null,
                Loop = entry.Look != null && entry.Look.FlightLoop != null ? _audio.StartLoop(entry.Look.FlightLoop, muzzle) : null,
            });
        }

        private void LaunchMissile(Entry entry, GuidedMissileDefinition missile)
        {
            var muzzle = Muzzle;
            var direction = (shooter.forward + Vector3.up * 0.1f).normalized;
            var rotation = Quaternion.LookRotation(direction);
            var look = entry.Look;
            _effects.SpawnOneShot(settings.RocketMuzzleFlashFor(look), muzzle, rotation);
            var launch = look != null && look.FireCue != null ? look.FireCue : sounds != null ? sounds.MissileLaunch : null;
            if (launch != null) _audio.Play(launch, muzzle);
            var flight = look != null && look.FlightLoop != null ? look.FlightLoop : sounds != null ? sounds.MissileFlight : null;
            var cluster = missile.Cluster;
            _rounds.Add(new Round
            {
                Visual = _effects.SpawnPooled(CombatEffectsSettings.Pick(look != null ? look.Projectile : null, settings.Missile), muzzle, rotation,
                    muteAudio: true),
                Position = muzzle, Velocity = direction * missile.Speed, HomingSpeed = missile.Speed, Remaining = missile.Lifetime,
                SplashRadius = missile.SplashRadius, Look = look, Impact = settings.MissileImpact,
                Blast = sounds != null ? sounds.MissileBlast : null,
                Loop = flight != null ? _audio.StartLoop(flight, muzzle) : null,
                Cluster = cluster,
                // Split part way to the target, so the review shows it (the match splits by time or distance).
                SplitIn = cluster.IsCluster ? Mathf.Min(cluster.SplitTime > 0f ? cluster.SplitTime : 1f,
                    0.6f * Vector3.Distance(muzzle, target.position) / Mathf.Max(missile.Speed, 1f)) : -1f,
            });
        }

        private void Split(Round carrier)
        {
            var look = carrier.Look;
            var forward = carrier.Velocity.normalized;
            var rotation = Quaternion.LookRotation(forward);
            if (look != null)
            {
                _effects.SpawnOneShot(look.SplitFlash, carrier.Position, rotation);
                if (look.SplitCue != null) _audio.Play(look.SplitCue, carrier.Position);
            }
            var model = look != null ? look.Bomblet : null;
            var cluster = carrier.Cluster;
            _seed = _seed * 1664525u + 1013904223u;
            for (var i = 0; i < cluster.Count; i++)
            {
                Vector3 velocity = Cluster.Direction(forward, cluster.ConeCos, _seed, i) * cluster.Speed;
                var visual = _effects.SpawnPooled(model != null ? model : settings.Rocket, carrier.Position, Quaternion.LookRotation(velocity));
                if (visual != null)
                    visual.transform.localScale = Vector3.one * (model != null ? 1f : RocketVisualSystem.RocketScale(cluster.SplashRadius));
                _rounds.Add(new Round
                {
                    Visual = visual, Position = carrier.Position, Velocity = velocity, Remaining = cluster.Lifetime,
                    SplashRadius = cluster.SplashRadius, Impact = settings.RocketImpact, Blast = sounds != null ? sounds.RocketBlast : null,
                });
            }
        }

        // ---- Flight ----

        private void MoveRounds(float dt)
        {
            if (dt <= 0f) return;
            for (var i = _rounds.Count - 1; i >= 0; i--)
            {
                var round = _rounds[i];
                // Drawn at the muzzle on its first frame, as in the match (else a 3,000 m/s round first shows 50 m out).
                if (round.Fresh)
                {
                    round.Fresh = false;
                    continue;
                }
                if (round.Bullet)
                {
                    var step = Mathf.Min(round.Velocity.magnitude * dt, round.Remaining);
                    round.Position += round.Velocity.normalized * step;
                    round.Remaining -= step;
                    if (round.Visual != null) round.Visual.transform.position = round.Position;
                    if (round.Remaining <= 0f) End(i, explode: true);
                    continue;
                }

                if (round.HomingSpeed > 0f)
                {
                    var want = (target.position - round.Position).normalized * round.HomingSpeed;
                    round.Velocity = Vector3.RotateTowards(round.Velocity, want, 3f * dt, round.HomingSpeed * dt);
                    round.Position += round.Velocity * dt;
                }
                else
                {
                    float3 velocity = round.Velocity;
                    round.Position = RocketFlight.Step(round.Position, ref velocity, round.Gravity, round.Drag, dt);
                    round.Velocity = velocity;
                }
                round.Remaining -= dt;

                if (round.SplitIn >= 0f && (round.SplitIn -= dt) <= 0f)
                {
                    Split(round);
                    End(i, explode: false);
                    continue;
                }
                var hit = round.Position.y <= 0.2f || Vector3.Distance(round.Position, target.position) < 1.5f;
                if (hit || round.Remaining <= 0f)
                {
                    End(i, explode: true);
                    continue;
                }
                if (round.Visual != null)
                {
                    round.Visual.transform.position = round.Position;
                    if (round.Velocity.sqrMagnitude > 1f) round.Visual.transform.rotation = Quaternion.LookRotation(round.Velocity);
                }
                round.Loop?.Set(round.Position, 1f, 1f);
            }
        }

        private void End(int index, bool explode)
        {
            var round = _rounds[index];
            if (explode)
            {
                var back = round.Velocity.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(-round.Velocity) : Quaternion.identity;
                _effects.SpawnOneShot(round.Impact, round.Position, back, muteAudio: round.Blast != null);
                if (round.Blast != null) _audio.Play(round.Blast, round.Position);
            }
            if (round.Loop != null) _audio.StopLoop(round.Loop);
            if (round.Visual != null) _effects.Release(round.Visual, round.Bullet ? 0f : settings.RocketTrailLinger);
            _rounds.RemoveAt(index);
        }

        private void DetonateAll()
        {
            for (var i = _rounds.Count - 1; i >= 0; i--) End(i, explode: true);
        }

        // ---- Screen list (a review tool: IMGUI, not game UI) ----

        private void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
            GUILayout.BeginArea(new Rect(16f, 16f, 520f, Screen.height - 32f), GUI.skin.box);
            GUILayout.Label("<b>WEAPON REVIEW</b>   Up/Down pick   Space fire (hold: guns)   X detonate   [ ] time " +
                            $"{timeScale:0.##}x   C view: {(cockpitView ? "COCKPIT" : "ORBIT (mouse, F1-F5)")}", _style);
            if (_entries.Count == 0) GUILayout.Label("No weapons: link the loadout catalog (Setup).", _style);
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                var mark = i == _selected ? "<color=#00ff94>> " : "   ";
                var end = i == _selected ? "</color>" : "";
                var kick = entry.Look != null && entry.Look.CameraKick > 0f ? $"  kick {entry.Look.CameraKick:0.###}" : "";
                GUILayout.Label($"{mark}{entry.Module.DisplayName.ToUpperInvariant()}  <size=12>[{entry.Family}]{kick}</size>{end}", _style);
            }
            GUILayout.EndArea();
        }
    }
}
