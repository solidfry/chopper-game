using System;
using Chopper.Presentation.Audio;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Hud;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.Presentation.Feedback
{
    /// <summary>
    /// Targeting and threat tones, driven by the HUD model (one source of truth for what the pilot is told):
    /// lock building (pitch rises), locked, being locked, and missile inbound (faster as it closes in).
    /// </summary>
    public sealed class TargetingFeedback : IDisposable
    {
        private readonly PresentationContext _context;
        private SoundLoop _lockLoop;
        private SoundLoop _lockedLoop;
        private SoundLoop _warningLoop;
        private SoundLoop _missileLoop;

        public TargetingFeedback(PresentationContext context)
        {
            _context = context;
            context.Hud.propertyChanged += OnHudChanged;
        }

        public void Dispose()
        {
            _context.Hud.propertyChanged -= OnHudChanged;
            Stop(ref _lockLoop);
            Stop(ref _lockedLoop);
            Stop(ref _warningLoop);
            Stop(ref _missileLoop);
        }

        private void OnHudChanged(object sender, BindablePropertyChangedEventArgs args)
        {
            var name = args.propertyName.ToString();
            if (name is nameof(ChopperHudModel.LockVisible) or nameof(ChopperHudModel.LockProgress) or nameof(ChopperHudModel.Locked)
                or nameof(ChopperHudModel.ThreatLocking) or nameof(ChopperHudModel.ThreatLocked)
                or nameof(ChopperHudModel.MissileInbound) or nameof(ChopperHudModel.MissileUrgency))
                Refresh();
        }

        private void Refresh()
        {
            var sounds = _context.Sounds;
            if (sounds == null) return;
            var hud = _context.Hud;

            var building = hud.LockVisible && !hud.Locked && hud.LockProgress > 0f;
            Toggle(ref _lockLoop, sounds.LockTone, building, pitch: Mathf.Lerp(0.8f, 1.4f, hud.LockProgress));
            Toggle(ref _lockedLoop, sounds.LockedTone, hud.Locked, pitch: 1f);

            // The missile tone replaces the lock warning: one alarm at a time.
            Toggle(ref _missileLoop, sounds.MissileWarning, hud.MissileInbound, pitch: Mathf.Lerp(1f, 1.6f, hud.MissileUrgency));
            Toggle(ref _warningLoop, sounds.LockWarning, !hud.MissileInbound && (hud.ThreatLocking || hud.ThreatLocked),
                pitch: hud.ThreatLocked ? 1.25f : 1f);
        }

        private void Toggle(ref SoundLoop loop, SoundCue cue, bool on, float pitch)
        {
            if (!on)
            {
                Stop(ref loop);
                return;
            }
            loop ??= _context.Audio.StartLoop(cue, Vector3.zero);
            loop?.Set(Vector3.zero, pitchScale: pitch);
        }

        private void Stop(ref SoundLoop loop)
        {
            if (loop == null) return;
            _context.Audio.StopLoop(loop);
            loop = null;
        }
    }
}
