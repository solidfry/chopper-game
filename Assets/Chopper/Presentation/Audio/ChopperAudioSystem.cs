using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// Client only. A rotor loop on every chopper (own one mostly 2D, others 3D): pitch rises with speed, and the rotor
    /// strains (louder, higher) with collective and boost. Boost: a kick when it starts and an engine roar while it lasts,
    /// on every chopper. An alarm loop while the own chopper is outside the arena. Field repair (own chopper): a servo
    /// hum while it repairs (pitch rises as the hull fills), a chime when full, a cut-off when a hit stops it. Pilot
    /// ability (own chopper): a sound and the pilot's radio call when it starts, a sound when it is ready again.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ChopperAudioSystem : PresentationSystemBase
    {
        /// <summary>Speed (m/s) at which the rotor reaches its highest pitch.</summary>
        private const float FullPitchSpeed = 80f;
        private const float MinRotorPitch = 0.92f;
        private const float MaxRotorPitch = 1.06f;
        /// <summary>Extra rotor pitch and volume at full collective / while boosting.</summary>
        private const float CollectivePitch = 0.03f;
        private const float BoostRotorPitch = 0.05f;
        private const float BoostRotorVolume = 0.25f;
        /// <summary>How fast the boost roar fades in and out (1/s).</summary>
        private const float BoostFadeIn = 14f;
        private const float BoostFadeOut = 4f;
        /// <summary>Speed (m/s) at which the boost roar reaches its highest pitch.</summary>
        private const float BoostFullPitchSpeed = 160f;
        /// <summary>Seconds the boost must be off before a new boost plays the kick again.</summary>
        private const float MinBoostGap = 0.3f;
        /// <summary>Death fall: the engine coughs (noise over time, this many changes a second), cuts out more often as the
        /// fall goes on, and winds down over <see cref="FailureWindDown"/> s to <see cref="FailureMinPitch"/>.</summary>
        private const float StutterRate = 6f;
        private const float FailureWindDown = 4f;
        private const float FailureMinPitch = 0.6f;
        /// <summary>The kill cam pulls back from the own wreck: its rotor is lifted by this so the fall stays heard.</summary>
        private const float OwnWreckBoost = 1.4f;

        private sealed class Voices
        {
            public SoundLoop Rotor;
            public SoundLoop Boost;
            public float BoostLevel;
            public bool WasBoosting;
            public float BoostEndTime = float.NegativeInfinity;
            public SoundLoop Repair;
            public RepairPhase RepairPhase;
            public uint AbilityUses;
            public bool AbilityCooling;
            public bool Jammed;
            public bool AbilitySeen;
            public SoundLoop Failure;
            public float Gate = 1f;
        }

        private readonly Dictionary<Entity, Voices> _choppers = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();
        private IAudioService _audio;
        private SoundLoop _alarm;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy() => StopAll();

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context) || context.Sounds == null)
            {
                StopAll();
                return;
            }

            // New context (scene change): its audio service has new voices.
            if (_audio != context.Audio)
            {
                StopAll();
                _audio = context.Audio;
            }

            var audio = _audio;
            var library = context.Sounds;
            var hasBounds = SystemAPI.TryGetSingleton<ArenaBounds>(out var bounds);
            var outOfBounds = false;
            var deltaTime = SystemAPI.Time.DeltaTime;

            var localId = SystemAPI.TryGetSingleton<NetworkId>(out var networkId) ? networkId.Value : -1;
            _seen.Clear();
            foreach (var (pose, velocity, flight, entity) in SystemAPI
                         .Query<RefRO<LocalToWorld>, RefRO<PhysicsVelocity>, RefRO<ChopperFlightState>>()
                         .WithAll<ChopperFlightConfig>()
                         .WithEntityAccess())
            {
                _seen.Add(entity);
                var position = pose.ValueRO.Position;
                var isLocal = SystemAPI.HasComponent<GhostOwnerIsLocal>(entity) && SystemAPI.IsComponentEnabled<GhostOwnerIsLocal>(entity);

                if (!_choppers.TryGetValue(entity, out var voices))
                {
                    voices = new Voices { Rotor = audio.StartLoop(isLocal ? library.RotorLocal : library.RotorRemote, position) };
                    _choppers[entity] = voices;
                }

                var state = flight.ValueRO;
                var speed = math.length(velocity.ValueRO.Linear);
                UpdateBoost(audio, library, voices, state.Boosting, position, speed, deltaTime);

                var speed01 = math.saturate(speed / FullPitchSpeed);
                var strain = math.max(state.Collective, 0f) * CollectivePitch + voices.BoostLevel * BoostRotorPitch;
                var rotorVolume = 1f + voices.BoostLevel * BoostRotorVolume;
                var rotorPitch = math.lerp(MinRotorPitch, MaxRotorPitch, speed01) + strain;
                var wreck = SystemAPI.HasComponent<Wreck>(entity) ? SystemAPI.GetComponent<Wreck>(entity) : default;
                if (wreck.Active)
                    UpdateFailure(audio, library, voices, entity, wreck.Age, position, isLocal || wreck.OwnerNetworkId == localId, deltaTime, ref rotorVolume, ref rotorPitch);
                else if (voices.Failure != null)
                {
                    audio.StopLoop(voices.Failure);
                    voices.Failure = null;
                    voices.Gate = 1f;
                }
                voices.Rotor?.Set(position, volumeScale: rotorVolume, pitchScale: rotorPitch);

                if (isLocal) outOfBounds = hasBounds && !bounds.Contains(position);
                if (isLocal) UpdateRepair(audio, library, voices, entity, position);
                if (isLocal) UpdateAbility(audio, library, voices, entity);
            }

            StopMissing(audio);
            UpdateAlarm(audio, library, outOfBounds);
        }

        /// <summary>
        /// A dead chopper falling (owner, 26 Nov: the engine went silent): the rotor stutters (cuts in and out, more often as
        /// it falls) and winds down, with a grinding, burning engine loop that coughs with it. Stops when the wreck is gone.
        /// </summary>
        private void UpdateFailure(IAudioService audio, SoundLibrary library, Voices voices, Entity entity, float age,
            Vector3 position, bool isLocal, float deltaTime, ref float rotorVolume, ref float rotorPitch)
        {
            if (voices.Failure == null && library.EngineFailure != null) voices.Failure = audio.StartLoop(library.EngineFailure, position);
            var fall = math.saturate(age / FailureWindDown);
            var noise = Mathf.PerlinNoise((float)SystemAPI.Time.ElapsedTime * StutterRate, entity.Index * 0.37f);
            var running = noise > math.lerp(0.35f, 0.6f, fall);
            voices.Gate = Mathf.MoveTowards(voices.Gate, running ? 1f : 0f, deltaTime * 30f);
            var gate = voices.Gate;
            rotorVolume *= math.lerp(0.1f, 1.1f, gate) * (isLocal ? OwnWreckBoost : 1f);
            rotorPitch *= math.lerp(1f, FailureMinPitch, fall) * math.lerp(0.85f, 1f, gate);
            voices.Failure?.Set(position, volumeScale: math.lerp(0.45f, 1f, gate) * (isLocal ? OwnWreckBoost : 1f),
                pitchScale: math.lerp(1.05f, 0.75f, fall) * math.lerp(0.9f, 1.05f, gate));
        }

        /// <summary>Kick on the rising edge; the roar fades in fast and out slowly, pitch rising with speed.</summary>
        private static void UpdateBoost(IAudioService audio, SoundLibrary library, Voices voices, bool boosting, Vector3 position,
            float speed, float deltaTime)
        {
            // Same rule as the camera kick: a one-frame flip from a prediction correction is not a new boost.
            if (boosting && !voices.WasBoosting && UnityEngine.Time.time - voices.BoostEndTime > MinBoostGap) audio.Play(library.BoostStart, position);
            if (!boosting && voices.WasBoosting) voices.BoostEndTime = UnityEngine.Time.time;
            voices.WasBoosting = boosting;

            var target = boosting ? 1f : 0f;
            var rate = boosting ? BoostFadeIn : BoostFadeOut;
            voices.BoostLevel = math.lerp(voices.BoostLevel, target, 1f - math.exp(-rate * deltaTime));

            if (voices.BoostLevel > 0.01f)
            {
                voices.Boost ??= audio.StartLoop(library.BoostLoop, position);
                voices.Boost?.Set(position, volumeScale: voices.BoostLevel,
                    pitchScale: 0.85f + 0.4f * math.saturate(speed / BoostFullPitchSpeed));
            }
            else if (voices.Boost != null)
            {
                audio.StopLoop(voices.Boost);
                voices.Boost = null;
            }
        }

        private void UpdateRepair(IAudioService audio, SoundLibrary library, Voices voices, Entity entity, Vector3 position)
        {
            var phase = SystemAPI.HasComponent<ChopperRepair>(entity) ? SystemAPI.GetComponent<ChopperRepair>(entity).State : RepairPhase.Idle;
            var health = SystemAPI.HasComponent<Health>(entity) ? SystemAPI.GetComponent<Health>(entity) : default;
            var share = health.Max > 0f ? math.saturate(health.Current / health.Max) : 1f;

            if (voices.RepairPhase == RepairPhase.Repairing && phase == RepairPhase.Locked) audio.Play(library.RepairInterrupted);
            else if (voices.RepairPhase == RepairPhase.Repairing && phase == RepairPhase.Idle && share >= 0.999f) audio.Play(library.RepairDone);
            voices.RepairPhase = phase;

            if (phase == RepairPhase.Repairing)
            {
                voices.Repair ??= audio.StartLoop(library.RepairLoop, position);
                voices.Repair?.Set(position, pitchScale: 0.85f + 0.35f * share);
            }
            else if (voices.Repair != null)
            {
                audio.StopLoop(voices.Repair);
                voices.Repair = null;
            }
        }

        /// <summary>Own pilot ability: a sound when it starts, when it is ready again, and when a Blackout jams you.</summary>
        private void UpdateAbility(IAudioService audio, SoundLibrary library, Voices voices, Entity entity)
        {
            if (!SystemAPI.HasComponent<AbilityState>(entity)) return;
            var ability = SystemAPI.GetComponent<AbilityState>(entity);
            var cooling = ability.CooldownLeft > 0f;
            // The first frame only records (a respawned chopper is not a new use).
            if (voices.AbilitySeen)
            {
                if (ability.Uses != voices.AbilityUses)
                {
                    audio.Play(library.AbilityActivate);
                    audio.PlayVoice(AbilityLine(entity));
                }
                else if (voices.AbilityCooling && !cooling) audio.Play(library.AbilityReady);
                if (ability.Jammed && !voices.Jammed) audio.Play(library.Jammed);
            }
            voices.AbilitySeen = true;
            voices.AbilityUses = ability.Uses;
            voices.AbilityCooling = cooling;
            voices.Jammed = ability.Jammed;
        }

        /// <summary>The own pilot's radio call for the ability (null without a pilot or a line).</summary>
        private AudioClip AbilityLine(Entity entity)
        {
            if (!SystemAPI.HasComponent<Chopper.Simulation.Loadout.ChopperLoadout>(entity) || !SystemAPI.HasSingleton<Chopper.Simulation.Session.LoadoutCatalogRef>()) return null;
            var catalog = SystemAPI.GetSingleton<Chopper.Simulation.Session.LoadoutCatalogRef>().Catalog.Value;
            var pilot = catalog != null ? catalog.FindPilot(SystemAPI.GetComponent<Chopper.Simulation.Loadout.ChopperLoadout>(entity).PilotId) : null;
            return pilot != null ? pilot.AbilityLine : null;
        }

        private void StopAll()
        {
            if (_audio != null)
            {
                foreach (var voices in _choppers.Values) Stop(_audio, voices);
                _audio.StopLoop(_alarm);
            }
            _choppers.Clear();
            _alarm = null;
        }

        private static void Stop(IAudioService audio, Voices voices)
        {
            audio.StopLoop(voices.Rotor);
            if (voices.Boost != null) audio.StopLoop(voices.Boost);
            if (voices.Repair != null) audio.StopLoop(voices.Repair);
            if (voices.Failure != null) audio.StopLoop(voices.Failure);
        }

        private void StopMissing(IAudioService audio)
        {
            _gone.Clear();
            foreach (var entity in _choppers.Keys)
                if (!_seen.Contains(entity)) _gone.Add(entity);
            foreach (var entity in _gone)
            {
                Stop(audio, _choppers[entity]);
                _choppers.Remove(entity);
            }
        }

        private void UpdateAlarm(IAudioService audio, SoundLibrary library, bool outOfBounds)
        {
            if (outOfBounds && _alarm == null)
            {
                _alarm = audio.StartLoop(library.OutOfBoundsAlarm, Vector3.zero);
            }
            else if (!outOfBounds && _alarm != null)
            {
                audio.StopLoop(_alarm);
                _alarm = null;
            }
        }
    }
}
