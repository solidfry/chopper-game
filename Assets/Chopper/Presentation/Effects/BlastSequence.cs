using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// A wreck's explosion over time (Docs/ChopperDeathDesign.md): the missile-impact flash, then each
    /// <see cref="CombatEffectsSettings.ChopperExplosionLayers"/> layer after its delay. Ground layers (lasting flames,
    /// smoke column) burn where the wreck comes down: at the blast point when it is on or near the ground, else where
    /// its fall lands, a little later; no ground under the fall = not shown. Without layers: three missile blasts along
    /// its travel. Shared by the game (<see cref="WreckEffectsSystem"/>, ECS physics) and the effects bench
    /// (<see cref="EffectsBench"/>, scene colliders): each passes its own <see cref="GroundCast"/>. With
    /// <c>muteAudio</c> the prefabs stay silent: the caller plays the wreck blast cue (shaped by distance and place).
    /// </summary>
    public sealed class BlastSequence
    {
        /// <summary>
        /// A segment test against the static world: true and the first hit's point and fraction (0..1 along the segment).
        /// </summary>
        public delegate bool GroundCast(Vector3 from, Vector3 to, out Vector3 point, out float fraction);

        /// <summary>A blast whose wreck lands within this time is a ground blast: everything burns at the blast point.</summary>
        public const float GroundFallSeconds = 0.4f;
        public const float MaxFallSeconds = 12f;
        private const float FallStep = 0.05f;
        private const float FallGravity = 9.81f;
        /// <summary>Drag size of the main body (a big piece, see <see cref="DebrisMotion.Drag"/>).</summary>
        private const float FallPieceSize = 3f;

        /// <summary>Delays (s) and offsets (along travel, m; up, m) of the fallback blasts when no layers are set.</summary>
        private static readonly (float Delay, float Ahead, float Up)[] Fallback = { (0f, 0f, 0f), (0.12f, 3f, 1.5f), (0.26f, -2f, 3f) };

        /// <summary>Waiting blasts: a layer index of the settings' layers, or -1 for a missile impact.</summary>
        private readonly List<(float At, Vector3 Position, int Layer, bool Mute)> _pending = new();

        public int Pending => _pending.Count;

        /// <summary>Starts a full wreck explosion at <paramref name="position"/> (the flash spawns now).</summary>
        public void Start(CombatEffects effects, Vector3 position, Vector3 wreckVelocity, float now, GroundCast cast, bool muteAudio = false)
        {
            var settings = effects.Settings;
            if (settings == null) return;
            var layers = settings.ChopperExplosionLayers;
            if (layers.Count > 0)
            {
                // The missile impact gives the first flash and its light; the layers make the body of the blast.
                effects.SpawnOneShot(settings.MissileImpact, position, Quaternion.identity, muteAudio);
                var lands = TryFindLanding(position, wreckVelocity, cast, out var landing, out var fallSeconds);
                var inAir = !lands || fallSeconds > GroundFallSeconds;
                for (var i = 0; i < layers.Count; i++)
                {
                    if (!layers[i].Ground || !inAir) _pending.Add((now + layers[i].Delay, position, i, muteAudio));
                    else if (lands) _pending.Add((now + fallSeconds + layers[i].Delay, landing, i, muteAudio));
                }
            }
            else if (settings.ChopperExplosion != settings.MissileImpact)
            {
                effects.SpawnOneShot(settings.ChopperExplosion, position, Quaternion.identity, muteAudio);
            }
            else
            {
                var travel = wreckVelocity.sqrMagnitude > 1f ? wreckVelocity.normalized : Vector3.forward;
                foreach (var (delay, ahead, up) in Fallback)
                    _pending.Add((now + delay, position + travel * ahead + Vector3.up * up, -1, muteAudio));
            }
        }

        /// <summary>One layer alone, now (the effects bench).</summary>
        public void StartLayer(int layer, Vector3 position, float now) => _pending.Add((now, position, layer, false));

        /// <summary>Spawns the blasts that are due.</summary>
        public void Tick(CombatEffects effects, float now)
        {
            var settings = effects.Settings;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].At > now) continue;
                var (_, position, layer, mute) = _pending[i];
                _pending.RemoveAt(i);
                if (settings == null) continue;
                if (layer >= 0 && layer < settings.ChopperExplosionLayers.Count) effects.SpawnLayer(settings.ChopperExplosionLayers[layer], position, mute);
                else effects.SpawnOneShot(settings.MissileImpact, position, Quaternion.identity, mute);
            }
        }

        public void Clear() => _pending.Clear();

        /// <summary>
        /// Where the wreck's main body comes down: its fall from the blast (it keeps <see cref="DebrisMotion.Inherit"/> of
        /// the wreck's speed, with a big piece's drag) traced in short steps. False if <paramref name="cast"/> finds no
        /// ground within <see cref="MaxFallSeconds"/> (off the map, a deep drop).
        /// </summary>
        public static bool TryFindLanding(Vector3 start, Vector3 wreckVelocity, GroundCast cast, out Vector3 landing, out float seconds)
        {
            landing = start;
            seconds = 0f;
            if (cast == null) return false;
            var position = start;
            var velocity = wreckVelocity * DebrisMotion.Inherit;
            for (var time = 0f; time < MaxFallSeconds; time += FallStep)
            {
                velocity = DebrisMotion.Drag(velocity, FallPieceSize, FallStep) + Vector3.down * (FallGravity * FallStep);
                var next = position + velocity * FallStep;
                if (cast(position, next, out var point, out var fraction))
                {
                    landing = point;
                    seconds = time + FallStep * fraction;
                    return true;
                }
                position = next;
            }
            return false;
        }
    }
}
