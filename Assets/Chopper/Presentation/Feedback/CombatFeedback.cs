using System;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Events;
using UnityEngine;

namespace Chopper.Presentation.Feedback
{
    /// <summary>
    /// Player feedback for combat: hit/kill markers and sounds for our hits; camera shake, sound and the damage
    /// direction ring for hits we take. Crashes play no hit sound here: the 3D crash sound of what you hit plays instead
    /// (<see cref="Audio.CrashAudioSystem"/>). Damage over time (corrosion, burn) ticks quietly: no sound, shake or
    /// ring per pulse. A hit from a jamming pilot (HEX) glitches the HUD.
    /// </summary>
    public sealed class CombatFeedback : IDisposable
    {
        private readonly PresentationContext _context;
        private readonly IDisposable _hits;
        private readonly IDisposable _damage;

        public CombatFeedback(PresentationContext context)
        {
            _context = context;
            _hits = context.Events.Subscribe<HitConfirmed>(OnHitConfirmed);
            _damage = context.Events.Subscribe<DamageTaken>(OnDamageTaken);
        }

        public void Dispose()
        {
            _hits.Dispose();
            _damage.Dispose();
        }

        private void OnHitConfirmed(HitConfirmed hit)
        {
            _context.Hud.ShowHit(hit.Killed);
            var sounds = _context.Sounds;
            // The hit cue has a short cooldown, so fast guns do not stack hit sounds.
            if (sounds != null) _context.Audio.Play(hit.Killed ? sounds.KillConfirm : sounds.HitConfirm);
        }

        /// <summary>HUD glitch from a jamming pilot's hit (HEX): strength and seconds.</summary>
        private const float JamStrength = 0.85f;
        private const float JamSeconds = 1.2f;

        private void OnDamageTaken(DamageTaken damage)
        {
            if (damage.Jam) _context.HudLook.Disrupt(JamStrength, JamSeconds);
            if (damage.Kind is Simulation.Combat.DamageKind.Corrosion or Simulation.Combat.DamageKind.Burn) return;
            var sounds = _context.Sounds;
            // Crashes and landings: the 3D crash sound of what you hit plays instead (Audio.CrashAudioSystem).
            var crash = damage.Kind is Simulation.Combat.DamageKind.Impact or Simulation.Combat.DamageKind.Landing;
            if (sounds != null && !crash) _context.Audio.Play(sounds.DamageTaken);

            var rig = _context.CameraRig;
            if (rig == null) return;
            // The shake matches the hit: guns and blasts by damage, crashes and landings by impact speed (CameraSettings).
            rig.AddShake(rig.ActiveSettings.HitTrauma(damage.Kind, damage.Amount, damage.ImpactSpeed));
            if (damage.HasSource)
                _context.Hud.ShowDamageFrom(DirectionFromCamera(rig.transform, damage.SourcePosition));
        }

        /// <summary>Angle round the camera's view axis: 0 = ahead, 90 = right, 180 = behind.</summary>
        public static float DirectionFromCamera(Transform camera, Vector3 source)
        {
            var local = camera.InverseTransformDirection(source - camera.position);
            return Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        }
    }
}
