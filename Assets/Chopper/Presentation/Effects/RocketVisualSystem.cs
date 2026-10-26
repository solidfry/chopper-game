using System.Collections.Generic;
using Chopper.Simulation.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Draws rockets from launch data (own predicted launches at once, other players' from the server):
    /// muzzle flash, the rocket visual in flight, and an impact where it hits. Visual only: the client ray-casts its own
    /// world to know where to stop; the server decides damage. Prefabs come from the scene's <see cref="CombatEffectsHost"/>.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class RocketVisualSystem : SystemBase
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
        private EntityQuery _localLaunches;
        private EntityQuery _remoteLaunches;
        private CombatEffectsHost _host;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
            _localLaunches = SystemAPI.QueryBuilder().WithAll<LocalRocketLaunch>().Build();
            _remoteLaunches = SystemAPI.QueryBuilder().WithAll<RocketLaunchRpc, ReceiveRpcCommandRequest>().Build();
        }

        protected override void OnUpdate()
        {
            if (_host == null)
                _host = Object.FindFirstObjectByType<CombatEffectsHost>();

            var settings = _host != null ? _host.Settings : null;
            var localId = SystemAPI.TryGetSingleton<NetworkId>(out var id) ? id.Value : 0;
            if (settings != null)
                SpawnLaunches(settings, localId);

            // Always consume, also without effects, so launch entities do not pile up.
            EntityManager.DestroyEntity(_localLaunches);
            EntityManager.DestroyEntity(_remoteLaunches);

            if (settings == null || !SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics)) return;
            var owners = SystemAPI.GetComponentLookup<GhostOwner>(isReadOnly: true);
            MoveRockets(settings, physics.CollisionWorld, owners, UnityEngine.Time.deltaTime);
        }

        private void SpawnLaunches(CombatEffectsSettings settings, int localId)
        {
            foreach (var launch in SystemAPI.Query<RefRO<LocalRocketLaunch>>())
                Spawn(settings, launch.ValueRO.Position, launch.ValueRO.Velocity, launch.ValueRO.Lifetime, localId);

            foreach (var launch in SystemAPI.Query<RefRO<RocketLaunchRpc>>().WithAll<ReceiveRpcCommandRequest>())
            {
                // Our own rockets are already drawn from prediction.
                if (launch.ValueRO.ShooterNetworkId == localId) continue;
                Spawn(settings, launch.ValueRO.Position, launch.ValueRO.Velocity, launch.ValueRO.Lifetime, launch.ValueRO.ShooterNetworkId);
            }
        }

        private void Spawn(CombatEffectsSettings settings, float3 position, float3 velocity, float lifetime, int shooterNetworkId)
        {
            var rotation = Quaternion.LookRotation(velocity);
            _host.SpawnOneShot(settings.RocketMuzzleFlash, position, rotation);
            _rockets.Add(new RocketVisual
            {
                GameObject = _host.Pool.Spawn(settings.Rocket, position, rotation),
                Position = position,
                Velocity = velocity,
                Remaining = lifetime,
                ShooterNetworkId = shooterNetworkId,
            });
        }

        private void MoveRockets(CombatEffectsSettings settings, in CollisionWorld world, ComponentLookup<GhostOwner> owners, float deltaTime)
        {
            for (var i = _rockets.Count - 1; i >= 0; i--)
            {
                var rocket = _rockets[i];
                var end = rocket.Position + rocket.Velocity * deltaTime;

                if (TryHit(world, owners, rocket.ShooterNetworkId, rocket.Position, end, out var impact))
                {
                    _host.SpawnOneShot(settings.RocketImpact, impact, Quaternion.LookRotation(-rocket.Velocity));
                    Finish(i, settings, impact);
                    continue;
                }

                rocket.Position = end;
                rocket.Remaining -= deltaTime;
                if (rocket.Remaining <= 0f)
                {
                    Finish(i, settings, end);
                    continue;
                }
                if (rocket.GameObject != null)
                    rocket.GameObject.transform.position = end;
            }
        }

        /// <summary>Stops the rocket; its smoke trail fades before the pool takes it back.</summary>
        private void Finish(int index, CombatEffectsSettings settings, float3 position)
        {
            var visual = _rockets[index].GameObject;
            if (visual != null)
            {
                visual.transform.position = position;
                _host.Pool.Release(visual, settings.RocketTrailLinger);
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
