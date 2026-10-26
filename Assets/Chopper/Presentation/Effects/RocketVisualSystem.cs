using System.Collections.Generic;
using Chopper.Simulation.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using UnityEngine;
using Collider = UnityEngine.Collider;
using Material = UnityEngine.Material;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Draws rockets from launch data (own predicted launches at once, other players' from the server)
    /// and a blast where they hit. Visual only: the client ray-casts its own world to know where to stop; the server
    /// decides damage. TODO: replace the placeholder shapes with rocket and explosion prefabs (VFX Graph).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class RocketVisualSystem : SystemBase
    {
        private const float BlastSeconds = 0.35f;

        private sealed class RocketVisual
        {
            public GameObject GameObject;
            public float3 Position;
            public float3 Velocity;
            public float Remaining;
            public float SplashRadius;
            /// <summary>Hits on this player's chopper are ignored (the rocket starts next to it).</summary>
            public int ShooterNetworkId;
        }

        private sealed class BlastVisual
        {
            public GameObject GameObject;
            public Material Material;
            public float Radius;
            public float Age;
        }

        private readonly List<RocketVisual> _rockets = new();
        private readonly List<BlastVisual> _blasts = new();
        private EntityQuery _localLaunches;
        private EntityQuery _remoteLaunches;
        private Material _blastMaterial;
        private Material _trailMaterial;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
            _localLaunches = SystemAPI.QueryBuilder().WithAll<LocalRocketLaunch>().Build();
            _remoteLaunches = SystemAPI.QueryBuilder().WithAll<RocketLaunchRpc, ReceiveRpcCommandRequest>().Build();
        }

        protected override void OnDestroy()
        {
            foreach (var rocket in _rockets) Object.Destroy(rocket.GameObject);
            foreach (var blast in _blasts) DestroyBlast(blast);
            if (_blastMaterial != null) Object.Destroy(_blastMaterial);
            if (_trailMaterial != null) Object.Destroy(_trailMaterial);
        }

        protected override void OnUpdate()
        {
            var localId = SystemAPI.TryGetSingleton<NetworkId>(out var id) ? id.Value : 0;
            SpawnLaunches(localId);
            if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics)) return;

            var deltaTime = UnityEngine.Time.deltaTime;
            var owners = SystemAPI.GetComponentLookup<GhostOwner>(isReadOnly: true);
            MoveRockets(physics.CollisionWorld, owners, deltaTime);
            GrowBlasts(deltaTime);
        }

        private void SpawnLaunches(int localId)
        {
            foreach (var launch in SystemAPI.Query<RefRO<LocalRocketLaunch>>())
                Spawn(launch.ValueRO.Position, launch.ValueRO.Velocity, launch.ValueRO.Lifetime, launch.ValueRO.SplashRadius, localId);

            foreach (var launch in SystemAPI.Query<RefRO<RocketLaunchRpc>>().WithAll<ReceiveRpcCommandRequest>())
            {
                // Our own rockets are already drawn from prediction.
                if (launch.ValueRO.ShooterNetworkId == localId) continue;
                Spawn(launch.ValueRO.Position, launch.ValueRO.Velocity, launch.ValueRO.Lifetime, launch.ValueRO.SplashRadius,
                    launch.ValueRO.ShooterNetworkId);
            }

            EntityManager.DestroyEntity(_localLaunches);
            EntityManager.DestroyEntity(_remoteLaunches);
        }

        private void MoveRockets(in CollisionWorld world, ComponentLookup<GhostOwner> owners, float deltaTime)
        {
            for (var i = _rockets.Count - 1; i >= 0; i--)
            {
                var rocket = _rockets[i];
                var end = rocket.Position + rocket.Velocity * deltaTime;

                if (TryHit(world, owners, rocket.ShooterNetworkId, rocket.Position, end, out var impact))
                {
                    SpawnBlast(impact, rocket.SplashRadius);
                    Remove(i);
                    continue;
                }

                rocket.Position = end;
                rocket.Remaining -= deltaTime;
                if (rocket.Remaining <= 0f)
                {
                    Remove(i);
                    continue;
                }
                rocket.GameObject.transform.position = end;
            }
        }

        private void GrowBlasts(float deltaTime)
        {
            for (var i = _blasts.Count - 1; i >= 0; i--)
            {
                var blast = _blasts[i];
                blast.Age += deltaTime;
                var t = Mathf.Clamp01(blast.Age / BlastSeconds);
                blast.GameObject.transform.localScale = Vector3.one * (blast.Radius * 2f * Mathf.Lerp(0.3f, 1f, t));
                var colour = blast.Material.color;
                colour.a = 1f - t;
                blast.Material.color = colour;

                if (t < 1f) continue;
                DestroyBlast(blast);
                _blasts.RemoveAt(i);
            }
        }

        private void Spawn(float3 position, float3 velocity, float lifetime, float splashRadius, int shooterNetworkId)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Rocket (visual)";
            Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetPositionAndRotation(position, Quaternion.LookRotation(velocity) * Quaternion.Euler(90f, 0f, 0f));
            body.transform.localScale = new Vector3(0.25f, 0.6f, 0.25f);

            var trail = body.AddComponent<TrailRenderer>();
            trail.time = 0.4f;
            trail.startWidth = 0.4f;
            trail.endWidth = 0f;
            trail.sharedMaterial = TrailMaterial();
            trail.startColor = new Color(1f, 0.85f, 0.5f, 0.9f);
            trail.endColor = new Color(0.6f, 0.6f, 0.6f, 0f);

            _rockets.Add(new RocketVisual
            {
                GameObject = body,
                Position = position,
                Velocity = velocity,
                Remaining = lifetime,
                SplashRadius = splashRadius,
                ShooterNetworkId = shooterNetworkId,
            });
        }

        private void SpawnBlast(float3 position, float radius)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Rocket blast (visual)";
            Object.Destroy(sphere.GetComponent<Collider>());
            sphere.transform.position = position;

            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Unity objects: compare with ==, not ??, so destroyed objects count as null.
            if (_blastMaterial == null)
                _blastMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.6f, 0.2f, 1f) };
            var material = new Material(_blastMaterial);
            renderer.sharedMaterial = material;

            _blasts.Add(new BlastVisual { GameObject = sphere, Material = material, Radius = math.max(1f, radius) });
        }

        private Material TrailMaterial()
        {
            if (_trailMaterial == null)
                _trailMaterial = new Material(Shader.Find("Sprites/Default"));
            return _trailMaterial;
        }

        private void Remove(int index)
        {
            Object.Destroy(_rockets[index].GameObject);
            _rockets.RemoveAt(index);
        }

        private static void DestroyBlast(BlastVisual blast)
        {
            Object.Destroy(blast.GameObject);
            Object.Destroy(blast.Material);
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
