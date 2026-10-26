using System;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Events;
using UnityEngine;

namespace Chopper.Presentation.Feedback
{
    /// <summary>
    /// Player feedback for combat: hit/kill markers and sounds for our hits; camera shake, sound and the damage
    /// direction ring for hits we take.
    /// </summary>
    public sealed class CombatFeedback : IDisposable
    {
        /// <summary>Damage that gives full camera trauma in one hit.</summary>
        private const float FullTraumaDamage = 40f;

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

        private void OnDamageTaken(DamageTaken damage)
        {
            var sounds = _context.Sounds;
            if (sounds != null) _context.Audio.Play(sounds.DamageTaken);

            var rig = _context.CameraRig;
            if (rig == null) return;
            rig.AddShake(Mathf.Clamp01(damage.Amount / FullTraumaDamage));
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
