using System;
using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Chopper.Presentation.Events;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Physics;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. A wreck's explosion where the server says it blew up (<see cref="WreckExploded"/>,
    /// Docs/ChopperDeathDesign.md): the explosion layers (fireball, flames, embers, smoke column; <see cref="BlastLayer"/>) after a missile-impact
    /// flash, or the chopper explosion, or (none set) three layered missile blasts, the main one at
    /// the point and two smaller ones a little along its travel; a heat mark; and camera shake that falls off with
    /// distance. The falling smoke and fire come from <see cref="DamageEffectsSystem"/>. The break-up: <see cref="WreckDebris"/>.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class WreckEffectsSystem : PresentationSystemBase
    {
        /// <summary>Delays (s) and offsets (along travel, m) of the layered fallback blasts.</summary>
        private static readonly (float Delay, float Ahead, float Up)[] Layers = { (0f, 0f, 0f), (0.12f, 3f, 1.5f), (0.26f, -2f, 3f) };

        /// <summary>A blast whose wreck lands within this time is a ground blast: everything burns at the blast point.</summary>
        private const float GroundFallSeconds = 0.4f;
        private const float MaxFallSeconds = 12f;
        private const float FallStep = 0.05f;
        private const float FallGravity = 9.81f;
        /// <summary>Drag size of the main body (a big piece, see <see cref="DebrisMotion.Drag"/>).</summary>
        private const float FallPieceSize = 3f;

        private readonly List<WreckExploded> _explosions = new();
        /// <summary>Waiting blasts: a layer index of the settings' layers, or -1 for a missile impact.</summary>
        private readonly List<(float At, Vector3 Position, int Layer)> _pending = new();
        private PresentationContext _context;
        private IDisposable _subscription;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy() => _subscription?.Dispose();

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            Bind(context);
            var effects = context.Effects;
            var settings = effects.Settings;
            if (settings == null) return;
            var now = UnityEngine.Time.time;

            foreach (var blast in _explosions)
            {
                var travel = blast.Velocity.sqrMagnitude > 1f ? blast.Velocity.normalized : Vector3.forward;
                var layers = settings.ChopperExplosionLayers;
                if (layers.Count > 0)
                {
                    // The missile impact gives the first flash and its light; the layers make the body of the blast.
                    effects.SpawnOneShot(settings.MissileImpact, blast.Position, Quaternion.identity);
                    // Ground layers (lasting flames, smoke column) burn where the wreck comes down: at the blast point
                    // when it is on or near the ground, else where the fall lands, a little later. No ground: none.
                    var lands = TryFindLanding(blast.Position, blast.Velocity, out var landing, out var fallSeconds);
                    var inAir = !lands || fallSeconds > GroundFallSeconds;
                    for (var i = 0; i < layers.Count; i++)
                    {
                        if (!layers[i].Ground || !inAir) _pending.Add((now + layers[i].Delay, blast.Position, i));
                        else if (lands) _pending.Add((now + fallSeconds + layers[i].Delay, landing, i));
                    }
                }
                else if (settings.ChopperExplosion != settings.MissileImpact)
                {
                    effects.SpawnOneShot(settings.ChopperExplosion, blast.Position, Quaternion.identity);
                }
                else
                {
                    foreach (var (delay, ahead, up) in Layers)
                        _pending.Add((now + delay, blast.Position + travel * ahead + Vector3.up * up, -1));
                }
                context.HeatMarks.AddTimed(blast.Position, VisionMath.BlastHeat * 2f, VisionMath.BlastSeconds * 2f);
                Shake(context, settings, blast.Position);
            }
            _explosions.Clear();

            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].At > now) continue;
                var (_, position, layer) = _pending[i];
                if (layer >= 0 && layer < settings.ChopperExplosionLayers.Count) effects.SpawnLayer(settings.ChopperExplosionLayers[layer], position);
                else effects.SpawnOneShot(settings.MissileImpact, position, Quaternion.identity);
                _pending.RemoveAt(i);
            }
        }

        /// <summary>
        /// Where the wreck's main body comes down: its fall from the blast (it keeps <see cref="DebrisMotion.Inherit"/> of
        /// the wreck's speed, with a big piece's drag) traced against the static world, in short steps. False if it finds
        /// no ground within <see cref="MaxFallSeconds"/> (off the map, a deep drop).
        /// </summary>
        private bool TryFindLanding(Vector3 start, Vector3 wreckVelocity, out Vector3 landing, out float seconds)
        {
            landing = start;
            seconds = 0f;
            if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics)) return false;
            var world = physics.CollisionWorld;
            var position = start;
            var velocity = wreckVelocity * DebrisMotion.Inherit;
            var hits = new NativeList<Unity.Physics.RaycastHit>(8, Allocator.Temp);
            try
            {
                for (var time = 0f; time < MaxFallSeconds; time += FallStep)
                {
                    velocity = DebrisMotion.Drag(velocity, FallPieceSize, FallStep) + Vector3.down * (FallGravity * FallStep);
                    var next = position + velocity * FallStep;
                    hits.Clear();
                    var ray = new RaycastInput { Start = position, End = next, Filter = CollisionFilter.Default };
                    if (world.CastRay(ray, ref hits))
                    {
                        var best = 2f;
                        foreach (var hit in hits)
                        {
                            // Static only: the wreck itself and other choppers are dynamic bodies.
                            if (hit.RigidBodyIndex < world.NumDynamicBodies || hit.Fraction >= best) continue;
                            best = hit.Fraction;
                            landing = hit.Position;
                        }
                        if (best <= 1f)
                        {
                            seconds = time + FallStep * best;
                            return true;
                        }
                    }
                    position = next;
                }
                return false;
            }
            finally
            {
                hits.Dispose();
            }
        }

        private static void Shake(PresentationContext context, CombatEffectsSettings settings, Vector3 position)
        {
            var rig = context.CameraRig;
            if (rig == null) return;
            var distance = Vector3.Distance(rig.transform.position, position);
            var share = 1f - Mathf.Clamp01(distance / settings.ChopperExplosionShakeRange);
            if (share > 0f) rig.AddShake(settings.ChopperExplosionShake * share * share);
        }

        /// <summary>New context (scene change): pending blasts belonged to the old one.</summary>
        private void Bind(PresentationContext context)
        {
            if (_context == context) return;
            _subscription?.Dispose();
            _explosions.Clear();
            _pending.Clear();
            _context = context;
            _subscription = context.Events.Subscribe<WreckExploded>(m => _explosions.Add(m));
        }
    }
}
