using System.Collections.Generic;
using Chopper.Simulation.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Material = UnityEngine.Material;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Draws a tracer for every shot of every chopper's nose gun, from <see cref="HitscanWeaponState.ShotCount"/>
    /// (predicted for the local player, replicated for others). Cosmetic: it ray-casts the client world to end the
    /// tracer; hits are decided on the server. TODO: replace with a tracer/muzzle flash/impact VFX Graph.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class GunTracerSystem : SystemBase
    {
        private const float TracerSeconds = 0.05f;
        /// <summary>A big jump (late join, packet loss) draws at most this many tracers.</summary>
        private const uint MaxTracersPerFrame = 3;

        private readonly Dictionary<Entity, uint> _lastShotCount = new();
        private readonly Dictionary<Entity, uint> _seen = new();
        private readonly List<(LineRenderer Line, float Remaining)> _active = new();
        private readonly Stack<LineRenderer> _pool = new();
        private Material _material;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy()
        {
            foreach (var (line, _) in _active) Object.Destroy(line.gameObject);
            foreach (var line in _pool) Object.Destroy(line.gameObject);
            if (_material != null) Object.Destroy(_material);
        }

        protected override void OnUpdate()
        {
            FadeTracers(UnityEngine.Time.deltaTime);
            if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics)) return;
            var world = physics.CollisionWorld;

            _seen.Clear();
            foreach (var (weapon, config, pose, entity) in SystemAPI
                         .Query<RefRO<HitscanWeaponState>, RefRO<HitscanWeaponConfig>, RefRO<LocalToWorld>>()
                         .WithEntityAccess())
            {
                var count = weapon.ValueRO.ShotCount;
                _seen[entity] = count;
                // First sight of this chopper: remember its count, draw nothing.
                if (!_lastShotCount.TryGetValue(entity, out var last) || count <= last) continue;

                var shots = math.min(count - last, MaxTracersPerFrame);
                var muzzle = math.transform(pose.ValueRO.Value, config.ValueRO.MuzzleOffset);
                var end = RayEnd(world, entity, muzzle, pose.ValueRO.Forward, config.ValueRO.Range);
                for (var i = 0u; i < shots; i++)
                    Draw(muzzle, end);
            }

            // Swap, so despawned choppers drop out.
            _lastShotCount.Clear();
            foreach (var (entity, count) in _seen) _lastShotCount[entity] = count;
        }

        private static float3 RayEnd(in CollisionWorld world, Entity shooter, float3 start, float3 forward, float range)
        {
            var end = start + forward * range;
            var ray = new RaycastInput { Start = start, End = end, Filter = CollisionFilter.Default };
            var hits = new NativeList<Unity.Physics.RaycastHit>(8, Allocator.Temp);
            var closest = 1f;
            if (world.CastRay(ray, ref hits))
            {
                foreach (var hit in hits)
                {
                    if (hit.Entity == shooter || hit.Fraction >= closest) continue;
                    closest = hit.Fraction;
                }
            }
            hits.Dispose();
            return math.lerp(start, end, closest);
        }

        private void Draw(Vector3 start, Vector3 end)
        {
            var line = _pool.Count > 0 ? _pool.Pop() : CreateLine();
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.gameObject.SetActive(true);
            _active.Add((line, TracerSeconds));
        }

        private void FadeTracers(float deltaTime)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var (line, remaining) = _active[i];
                remaining -= deltaTime;
                if (remaining > 0f)
                {
                    _active[i] = (line, remaining);
                    continue;
                }
                line.gameObject.SetActive(false);
                _pool.Push(line);
                _active.RemoveAt(i);
            }
        }

        private LineRenderer CreateLine()
        {
            if (_material == null)
                _material = new Material(Shader.Find("Sprites/Default"));

            var line = new GameObject("Tracer (visual)").AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.positionCount = 2;
            line.startWidth = 0.08f;
            line.endWidth = 0.04f;
            line.startColor = new Color(1f, 0.95f, 0.6f, 1f);
            line.endColor = new Color(1f, 0.8f, 0.3f, 0.4f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }
    }
}
