using System;
using System.Collections.Generic;
using Chopper.Presentation.Audio;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Events;
using Chopper.Simulation.Combat;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. A pooled visual follows every guided missile ghost (the shooter's predicted copy and everyone
    /// else's interpolated one), a launch flash when it appears, its trail fades when the ghost goes, and an impact
    /// where the server says it exploded (<see cref="MissileDetonated"/>). Sound from the cues (launch, a motor loop that
    /// follows the missile, the blast), so far and hidden missiles get the place rules; the prefabs' own audio is muted.
    /// A thick smoke column follows each missile (<see cref="MissileSmokeTrails{TKey}"/>).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class MissileEffectsSystem : PresentationSystemBase
    {
        private readonly Dictionary<Entity, GameObject> _visuals = new();
        private readonly Dictionary<Entity, SoundLoop> _loops = new();
        private readonly MissileSmokeTrails<Entity> _smoke = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();
        private readonly List<Vector3> _detonations = new();
        private PresentationContext _context;
        private IDisposable _subscription;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy()
        {
            _subscription?.Dispose();
            StopLoops();
            _smoke.Clear();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            Bind(context);
            var effects = context.Effects;
            var settings = effects.Settings;
            if (settings == null) return;

            var audio = context.Audio;
            var sounds = context.Sounds;
            var smoke = MissileSmokeTrails<Entity>.Enabled(settings);
            if (smoke) _smoke.Begin();
            _seen.Clear();
            foreach (var (pose, missile, entity) in SystemAPI.Query<RefRO<LocalToWorld>, RefRO<GuidedMissile>>().WithEntityAccess())
            {
                _seen.Add(entity);
                Vector3 position = pose.ValueRO.Position;
                Quaternion rotation = pose.ValueRO.Rotation;
                if (!_visuals.TryGetValue(entity, out var visual))
                {
                    // The missile family's look (Docs/WeaponFeelPlan.md step 4); empty slots use the shared ones.
                    var look = settings.Look(missile.ValueRO.ModuleId, WeaponKind.Guided);
                    effects.SpawnOneShot(settings.RocketMuzzleFlashFor(look), position, rotation);
                    visual = effects.SpawnPooled(CombatEffectsSettings.Pick(look != null ? look.Projectile : null, settings.Missile),
                        position, rotation, muteAudio: true);
                    _visuals[entity] = visual;
                    if (audio != null && sounds != null)
                    {
                        audio.Play(look != null && look.FireCue != null ? look.FireCue : sounds.MissileLaunch, position);
                        var loop = audio.StartLoop(look != null && look.FlightLoop != null ? look.FlightLoop : sounds.MissileFlight, position);
                        if (loop != null) _loops[entity] = loop;
                    }
                }
                if (visual != null) visual.transform.SetPositionAndRotation(position, rotation);
                if (_loops.TryGetValue(entity, out var flight)) flight.Set(position, 1f, 1f);
                var forward = rotation * Vector3.forward;
                if (smoke) _smoke.Update(settings, entity, position - forward * 0.8f, forward);
            }
            if (smoke) _smoke.End(settings);

            // Ghost removed (exploded or expired on the server): let the trail fade.
            _gone.Clear();
            foreach (var entity in _visuals.Keys)
                if (!_seen.Contains(entity)) _gone.Add(entity);
            foreach (var entity in _gone)
            {
                effects.Release(_visuals[entity], settings.RocketTrailLinger);
                _visuals.Remove(entity);
                if (_loops.Remove(entity, out var flight)) audio?.StopLoop(flight);
            }

            foreach (var position in _detonations)
            {
                effects.SpawnOneShot(settings.MissileImpact, position, Quaternion.identity, muteAudio: sounds != null && sounds.MissileBlast != null);
                if (audio != null && sounds != null) audio.Play(sounds.MissileBlast, position);
                context.HeatMarks.AddTimed(position, VisionMath.BlastHeat, VisionMath.BlastSeconds);
            }
            _detonations.Clear();
        }

        /// <summary>New context (scene change): old visuals went with the old pool.</summary>
        private void Bind(PresentationContext context)
        {
            if (_context == context) return;
            _subscription?.Dispose();
            StopLoops();
            _smoke.Clear();
            _visuals.Clear();
            _detonations.Clear();
            _context = context;
            _subscription = context.Events.Subscribe<MissileDetonated>(m => _detonations.Add(m.Position));
        }

        private void StopLoops()
        {
            if (_context?.Audio != null)
                foreach (var loop in _loops.Values) _context.Audio.StopLoop(loop);
            _loops.Clear();
        }
    }
}
