using System;
using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Events;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Draws rockets from <see cref="RocketLaunched"/> messages: muzzle flash, the rocket visual in flight,
    /// and an impact where it hits. Visual only: the client ray-casts its own world to know where to stop; the server
    /// decides damage. Prefabs come from the presentation context.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class RocketVisualSystem : PresentationSystemBase
    {
        private sealed class RocketVisual
        {
            public GameObject GameObject;
            public float3 Position;
            public float3 Velocity;
            public float Remaining;
            /// <summary>Hits on this player's chopper are ignored (the rocket starts next to it).</summary>
            public int ShooterNetworkId;
        }

        private readonly List<RocketVisual> _rockets = new();
        private readonly List<RocketLaunched> _launches = new();
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
            if (effects.Settings == null)
            {
                _launches.Clear();
                return;
            }

            foreach (var launch in _launches)
                Spawn(effects, launch);
            _launches.Clear();

            if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics)) return;
            var owners = SystemAPI.GetComponentLookup<GhostOwner>(isReadOnly: true);
            MoveRockets(effects, physics.CollisionWorld, owners, UnityEngine.Time.deltaTime);
        }

        /// <summary>Subscribes to the context's launches; a new context (scene change) drops the old rockets.</summary>
        private void Bind(PresentationContext context)
        {
            if (_context == context) return;
            _subscription?.Dispose();
            _rockets.Clear();
            _launches.Clear();
            _context = context;
            // Messages arrive earlier in the frame (PresentationEventSystem); spawn them in OnUpdate.
            _subscription = context.Events.Subscribe<RocketLaunched>(_launches.Add);
        }

        private void Spawn(CombatEffects effects, in RocketLaunched launch)
        {
            var rotation = Quaternion.LookRotation(launch.Velocity);
            effects.SpawnOneShot(effects.Settings.RocketMuzzleFlash, launch.Position, rotation);
            _rockets.Add(new RocketVisual
            {
                GameObject = effects.SpawnPooled(effects.Settings.Rocket, launch.Position, rotation),
                Position = launch.Position,
                Velocity = launch.Velocity,
                Remaining = launch.Lifetime,
                ShooterNetworkId = launch.ShooterNetworkId,
            });
        }

        private void MoveRockets(CombatEffects effects, in CollisionWorld world, ComponentLookup<GhostOwner> owners, float deltaTime)
        {
            for (var i = _rockets.Count - 1; i >= 0; i--)
            {
                var rocket = _rockets[i];
                var end = rocket.Position + rocket.Velocity * deltaTime;

                if (TryHit(world, owners, rocket.ShooterNetworkId, rocket.Position, end, out var impact))
                {
                    effects.SpawnOneShot(effects.Settings.RocketImpact, impact, Quaternion.LookRotation(-rocket.Velocity));
                    Finish(effects, i, impact);
                    continue;
                }

                rocket.Position = end;
                rocket.Remaining -= deltaTime;
                if (rocket.Remaining <= 0f)
                {
                    Finish(effects, i, end);
                    continue;
                }
                if (rocket.GameObject != null)
                    rocket.GameObject.transform.position = end;
            }
        }

        /// <summary>Stops the rocket; its smoke trail fades before the pool takes it back.</summary>
        private void Finish(CombatEffects effects, int index, float3 position)
        {
            var visual = _rockets[index].GameObject;
            if (visual != null)
            {
                visual.transform.position = position;
                effects.Release(visual, effects.Settings.RocketTrailLinger);
            }
            _rockets.RemoveAt(index);
        }

        private static bool TryHit(in CollisionWorld world, ComponentLookup<GhostOwner> owners, int shooterNetworkId,
            float3 start, float3 end, out float3 impact)
        {
            var ray = new RaycastInput { Start = start, End = end, Filter = CollisionFilter.Default };
            var hits = new NativeList<Unity.Physics.RaycastHit>(8, Allocator.Temp);
            var closest = float.MaxValue;
            impact = end;

            if (world.CastRay(ray, ref hits))
            {
                foreach (var hit in hits)
                {
                    if (hit.Fraction >= closest) continue;
                    if (owners.TryGetComponent(hit.Entity, out var owner) && owner.NetworkId == shooterNetworkId) continue;
                    closest = hit.Fraction;
                    impact = hit.Position;
                }
            }
            hits.Dispose();
            return closest < float.MaxValue;
        }
    }
}
