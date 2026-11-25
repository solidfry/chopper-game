using System;
using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Effects;
using Chopper.Presentation.Events;
using Chopper.Simulation.Match;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// Client only. Capture the Nuke sounds (<see cref="NukeSound"/> rules):
    /// <list type="bullet">
    /// <item>the blast (<see cref="NukeDetonated"/>), silent until its shock front reaches the listener, then a close
    /// roar and a far rumble mixed by distance;</item>
    /// <item>a short call when the nuke changes (<see cref="NukeChanged"/>: hooked, dropped, armed, defused, reset);</item>
    /// <item>a siren while a nuke is armed, louder for the team it threatens;</item>
    /// <item>beeps at the nuke while an arm, defuse or hook bar fills, faster as it fills (3D: heard near the nuke).</item>
    /// </list>
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class NukeAudioSystem : PresentationSystemBase
    {
        private readonly List<NukeChanged> _changes = new();
        private readonly List<NukeDetonated> _detonations = new();
        private readonly List<(float At, float Near, float Far)> _blasts = new();
        private PresentationContext _context;
        private IDisposable _changeSubscription;
        private IDisposable _blastSubscription;
        private SoundLoop _siren;
        private NukeState _before;
        private bool _hasBefore;
        private float _nextBeep;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy()
        {
            Unbind();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            Bind(context);
            var sounds = context.Sounds;
            var audio = context.Audio;
            if (sounds == null || audio == null)
            {
                _changes.Clear();
                _detonations.Clear();
                return;
            }
            var now = UnityEngine.Time.time;
            var listener = ListenerPosition();

            foreach (var detonation in _detonations)
            {
                var distance = Vector3.Distance(listener, detonation.Position);
                var speed = context.Effects.Settings != null ? context.Effects.Settings.NukeShockSpeed : 340f;
                NukeSound.BlastMix(distance, out var near, out var far);
                _blasts.Add((now + NukeBlastTimeline.ShockArrival(distance, speed), near, far));
            }
            _detonations.Clear();
            for (var i = _blasts.Count - 1; i >= 0; i--)
            {
                if (_blasts[i].At > now) continue;
                // Both cues are 2D: a nuke fills the sky, and the volume already follows the distance.
                if (_blasts[i].Near > 0.01f) audio.Play(sounds.NukeBlastNear, listener, _blasts[i].Near);
                if (_blasts[i].Far > 0.01f) audio.Play(sounds.NukeBlastFar, listener, _blasts[i].Far);
                _blasts.RemoveAt(i);
            }

            foreach (var change in _changes) PlayStinger(audio, sounds, NukeSound.Stinger(change.Change));
            _changes.Clear();

            if (context.Match.Mode != MatchMode.CaptureTheNuke || !TryGetNuke(out var nuke, out var at))
            {
                StopSiren(audio);
                _hasBefore = false;
                return;
            }

            var siren = NukeSound.SirenVolume(nuke, LocalTeam());
            if (siren > 0f)
            {
                _siren ??= audio.StartLoop(sounds.NukeSiren, listener);
                _siren?.Set(listener, siren);
            }
            else StopSiren(audio);

            var progress = _hasBefore ? NukeSound.BeepProgress(nuke, _before) : -1f;
            if (progress >= 0f && now >= _nextBeep)
            {
                audio.Play(sounds.NukeBeep, at, 1f);
                _nextBeep = now + NukeSound.BeepInterval(progress);
            }
            _before = nuke;
            _hasBefore = true;
        }

        private static void PlayStinger(IAudioService audio, SoundLibrary sounds, NukeStinger stinger)
        {
            switch (stinger)
            {
                case NukeStinger.Hooked: audio.Play(sounds.NukeHooked); break;
                case NukeStinger.Dropped: audio.Play(sounds.NukeDropped); break;
                case NukeStinger.Armed: audio.Play(sounds.NukeArmed); break;
                case NukeStinger.Defused: audio.Play(sounds.NukeDefused); break;
                // Back on its pad: the drop call, lower.
                case NukeStinger.Reset: audio.PlayPitched(sounds.NukeDropped, 0.75f); break;
            }
        }

        private bool TryGetNuke(out NukeState nuke, out Vector3 position)
        {
            foreach (var (state, pose) in SystemAPI.Query<RefRO<NukeState>, RefRO<LocalToWorld>>())
            {
                nuke = state.ValueRO;
                position = pose.ValueRO.Position;
                return true;
            }
            nuke = default;
            position = default;
            return false;
        }

        private byte LocalTeam()
        {
            foreach (var team in SystemAPI.Query<RefRO<Team>>().WithAll<GhostOwnerIsLocal>())
                return team.ValueRO.Value;
            return TeamRules.None;
        }

        private static Vector3 ListenerPosition()
        {
            var camera = UnityEngine.Camera.main;
            return camera != null ? camera.transform.position : Vector3.zero;
        }

        private void StopSiren(IAudioService audio)
        {
            if (_siren == null) return;
            audio.StopLoop(_siren);
            _siren = null;
        }

        /// <summary>New context (scene change): a new message channel; queued sounds belonged to the old scene.</summary>
        private void Bind(PresentationContext context)
        {
            if (_context == context) return;
            Unbind();
            _context = context;
            _changeSubscription = context.Events.Subscribe<NukeChanged>(_changes.Add);
            _blastSubscription = context.Events.Subscribe<NukeDetonated>(_detonations.Add);
        }

        private void Unbind()
        {
            _changeSubscription?.Dispose();
            _blastSubscription?.Dispose();
            _changeSubscription = null;
            _blastSubscription = null;
            if (_context?.Audio != null && _siren != null) _context.Audio.StopLoop(_siren);
            _siren = null;
            _context = null;
            _changes.Clear();
            _detonations.Clear();
            _blasts.Clear();
            _hasBefore = false;
        }
    }
}
