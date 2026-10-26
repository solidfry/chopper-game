using System.Collections.Generic;
using Chopper.Simulation.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Nose gun effects for every chopper, from <see cref="HitscanWeaponState.ShotCount"/> (predicted for
    /// the local player, replicated for others): muzzle flash, a bullet visual flown to where the shot ends, and an
    /// impact if it hit something. Cosmetic: the client ray-casts its own world; the server decides hits.
    /// Prefabs come from the scene's <see cref="CombatEffectsHost"/>.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class GunEffectsSystem : SystemBase
    {
        /// <summary>A big jump (late join, packet loss) draws at most this many shots.</summary>
        private const uint MaxShotsPerFrame = 3;

        private sealed class Bullet
        {
            public GameObject Visual;
            public Vector3 Position;
            public Vector3 Direction;
            public float DistanceLeft;
            public bool Impact;
        }

        private readonly Dictionary<Entity, uint> _lastShotCount = new();
        private readonly Dictionary<Entity, uint> _seen = new();
        private readonly Dictionary<Entity, float> _lastFlashTime = new();
        private readonly List<Bullet> _bullets = new();
        private CombatEffectsHost _host;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (_host == null)
            {
                _host = Object.FindFirstObjectByType<CombatEffectsHost>();
                if (_host == null || _host.Settings == null) return;
            }

            var settings = _host.Settings;
            MoveBullets(settings, UnityEngine.Time.deltaTime);
            if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics)) return;
            var world = physics.CollisionWorld;
            var now = UnityEngine.Time.time;

            _seen.Clear();
            foreach (var (weapon, config, pose, entity) in SystemAPI
                         .Query<RefRO<HitscanWeaponState>, RefRO<HitscanWeaponConfig>, RefRO<LocalToWorld>>()
                         .WithEntityAccess())
            {
                var count = weapon.ValueRO.ShotCount;
                _seen[entity] = count;
                // First sight of this chopper: remember its count, draw nothing.
                if (!_lastShotCount.TryGetValue(entity, out var last) || count <= last) continue;

                var muzzle = math.transform(pose.ValueRO.Value, config.ValueRO.MuzzleOffset);
                var forward = pose.ValueRO.Forward;
                var rotation = quaternion.LookRotationSafe(forward, pose.ValueRO.Up);

                if (!_lastFlashTime.TryGetValue(entity, out var lastFlash) || now - lastFlash >= settings.GunMuzzleFlashInterval)
                {
                    _host.SpawnOneShot(settings.GunMuzzleFlash, muzzle, rotation);
                    _lastFlashTime[entity] = now;
                }

                var hit = RayCast(world, entity, muzzle, forward, config.ValueRO.Range, out var distance);
                var shots = math.min(count - last, MaxShotsPerFrame);
                for (var i = 0u; i < shots; i++)
                    Fire(settings, muzzle, forward, rotation, distance, hit);
            }

            // Swap, so despawned choppers drop out.
            _lastShotCount.Clear();
            foreach (var (entity, count) in _seen) _lastShotCount[entity] = count;
            if (_lastFlashTime.Count > _seen.Count) PruneFlashTimes();
        }

        private void PruneFlashTimes()
        {
            var gone = new List<Entity>();
            foreach (var entity in _lastFlashTime.Keys)
                if (!_seen.ContainsKey(entity)) gone.Add(entity);
            foreach (var entity in gone) _lastFlashTime.Remove(entity);
        }

        private void Fire(CombatEffectsSettings settings, Vector3 muzzle, Vector3 forward, Quaternion rotation, float distance, bool hit)
        {
            var visual = _host.Pool.Spawn(settings.GunBullet, muzzle, rotation);
            if (visual == null)
            {
                // No bullet visual set: show the impact at once.
                if (hit) _host.SpawnOneShot(settings.GunImpact, muzzle + forward * distance, Quaternion.LookRotation(-forward));
                return;
            }

            _bullets.Add(new Bullet { Visual = visual, Position = muzzle, Direction = forward, DistanceLeft = distance, Impact = hit });
        }

        private void MoveBullets(CombatEffectsSettings settings, float deltaTime)
        {
            var step = settings.GunBulletSpeed * deltaTime;
            for (var i = _bullets.Count - 1; i >= 0; i--)
            {
                var bullet = _bullets[i];
                var move = Mathf.Min(step, bullet.DistanceLeft);
                bullet.Position += bullet.Direction * move;
                bullet.DistanceLeft -= move;

                if (bullet.DistanceLeft > 0f)
                {
                    if (bullet.Visual != null) bullet.Visual.transform.position = bullet.Position;
                    continue;
                }

                if (bullet.Impact)
                    _host.SpawnOneShot(settings.GunImpact, bullet.Position, Quaternion.LookRotation(-bullet.Direction));
                _host.Pool.Release(bullet.Visual);
                _bullets.RemoveAt(i);
            }
        }

        /// <summary>Distance to the first thing the shot hits (not the shooter), or the full range.</summary>
        private static bool RayCast(in CollisionWorld world, Entity shooter, float3 start, float3 forward, float range, out float distance)
        {
            var ray = new RaycastInput { Start = start, End = start + forward * range, Filter = CollisionFilter.Default };
            var hits = new NativeList<Unity.Physics.RaycastHit>(8, Allocator.Temp);
            var closest = 1f;
            var found = false;
            if (world.CastRay(ray, ref hits))
            {
                foreach (var hit in hits)
                {
                    if (hit.Entity == shooter || hit.Fraction >= closest) continue;
                    closest = hit.Fraction;
                    found = true;
                }
            }
            hits.Dispose();
            distance = closest * range;
            return found;
        }
    }
}
