using System.Collections.Generic;
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
    /// Client only. A rotor loop on every chopper (own one mostly 2D, others 3D), pitch rising with speed; the dash
    /// sound; and an alarm loop while the own chopper is outside the arena.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ChopperAudioSystem : SystemBase
    {
        /// <summary>Speed (m/s) at which the rotor reaches its highest pitch.</summary>
        private const float FullPitchSpeed = 80f;
        private const float MinRotorPitch = 0.9f;
        private const float MaxRotorPitch = 1.2f;

        private readonly Dictionary<Entity, SoundLoop> _rotors = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();
        private AudioHost _host;
        private SoundLoop _alarm;
        private float _lastDashCooldown;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy()
        {
            if (_host == null) return;
            foreach (var loop in _rotors.Values) _host.Audio.StopLoop(loop);
            _host.Audio.StopLoop(_alarm);
        }

        protected override void OnUpdate()
        {
            if (_host == null)
                _host = Object.FindFirstObjectByType<AudioHost>();
            if (_host == null || _host.Library == null) return;

            var audio = _host.Audio;
            var library = _host.Library;
            var hasBounds = SystemAPI.TryGetSingleton<ArenaBounds>(out var bounds);
            var outOfBounds = false;

            _seen.Clear();
            foreach (var (pose, velocity, entity) in SystemAPI
                         .Query<RefRO<LocalToWorld>, RefRO<PhysicsVelocity>>()
                         .WithAll<ChopperFlightConfig>()
                         .WithEntityAccess())
            {
                _seen.Add(entity);
                var position = pose.ValueRO.Position;
                var isLocal = SystemAPI.HasComponent<GhostOwnerIsLocal>(entity) && SystemAPI.IsComponentEnabled<GhostOwnerIsLocal>(entity);

                if (!_rotors.TryGetValue(entity, out var rotor))
                {
                    rotor = audio.StartLoop(isLocal ? library.RotorLocal : library.RotorRemote, position);
                    _rotors[entity] = rotor;
                }

                var speed01 = math.saturate(math.length(velocity.ValueRO.Linear) / FullPitchSpeed);
                rotor?.Set(position, pitchScale: math.lerp(MinRotorPitch, MaxRotorPitch, speed01));

                if (!isLocal) continue;
                outOfBounds = hasBounds && !bounds.Contains(position);
                PlayDashIfUsed(audio, library, entity, position);
            }

            StopMissingRotors(audio);
            UpdateAlarm(audio, library, outOfBounds);
        }

        private void PlayDashIfUsed(IAudioService audio, SoundLibrary library, Entity entity, Vector3 position)
        {
            if (!SystemAPI.HasComponent<ChopperFlightState>(entity)) return;
            var cooldown = SystemAPI.GetComponent<ChopperFlightState>(entity).DashCooldownRemaining;
            // The cooldown jumps up only when a dash starts.
            if (cooldown > _lastDashCooldown + 0.1f)
                audio.Play(library.Dash, position);
            _lastDashCooldown = cooldown;
        }

        private void StopMissingRotors(IAudioService audio)
        {
            _gone.Clear();
            foreach (var entity in _rotors.Keys)
                if (!_seen.Contains(entity)) _gone.Add(entity);
            foreach (var entity in _gone)
            {
                audio.StopLoop(_rotors[entity]);
                _rotors.Remove(entity);
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
