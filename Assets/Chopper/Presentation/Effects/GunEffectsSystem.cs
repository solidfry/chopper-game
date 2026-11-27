using System.Collections.Generic;
using Chopper.Presentation.Audio;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Visuals;
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
    /// impact if it hit something, with the impact sound of what it hit (<see cref="Surfaces.Classify"/>: a ping on
    /// metal, chips off concrete, a thud in dirt, a splash). A gun with a bullet speed flies its tracers like the server's
    /// rounds (gun speed plus the chopper's velocity) and ray-casts each frame's step; an instant gun ray-casts once and
    /// flies the visual at the settings' speed. Cosmetic: the client ray-casts its own world; the server decides hits.
    /// Prefabs and sounds come from the presentation context.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class GunEffectsSystem : PresentationSystemBase
    {
        /// <summary>A big jump (late join, packet loss) draws at most this many shots.</summary>
        private const uint MaxShotsPerFrame = 3;
        /// <summary>
        /// Cockpit view, own gun: bullets start this far ahead of the muzzle. The flash and the trail start stay where they
        /// spawn (world space) and, when the chopper goes down, rose into the canopy view.
        /// </summary>
        private const float CockpitBulletStart = 8f;

        private sealed class Bullet
        {
            public GameObject Visual;
            public Vector3 Position;
            public Vector3 Direction;
            public float DistanceLeft;
            /// <summary>m/s: the gun's round speed (with the chopper's velocity), or the settings' speed for an instant gun.</summary>
            public float Speed;
            public bool Impact;
            public SurfaceKind Surface;
            /// <summary>A round with travel time: ray-cast each frame's step, so it stops on what is there when it arrives.</summary>
            public bool CastEachFrame;
            public Entity Shooter;
            /// <summary>The gun's family look (impact on arrival); null = shared.</summary>
            public WeaponLook Look;
        }

        private readonly Dictionary<Entity, uint> _lastShotCount = new();
        private readonly Dictionary<Entity, uint> _seen = new();
        private readonly Dictionary<Entity, float> _lastFlashTime = new();
        private readonly List<Bullet> _bullets = new();
        private CombatEffects _effects;
        private PresentationContext _context;
        private ComponentLookup<PhysicsSurface> _surfaces;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
            _surfaces = GetComponentLookup<PhysicsSurface>(isReadOnly: true);
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context) || context.Effects.Settings == null) return;
            if (_effects != context.Effects)
            {
                // New context: the old pool is gone with its bullets.
                _bullets.Clear();
                _effects = context.Effects;
            }

            _context = context;
            _surfaces.Update(this);
            var audio = context.Audio;
            var gunSound = context.Sounds != null ? context.Sounds.GunFire : null;
            var settings = _effects.Settings;
            var hasPhysics = SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics);
            MoveBullets(settings, UnityEngine.Time.deltaTime, hasPhysics, hasPhysics ? physics.CollisionWorld : default);
            if (!hasPhysics) return;
            var world = physics.CollisionWorld;
            var now = UnityEngine.Time.time;
            var cockpitView = context.CameraRig != null && context.CameraRig.HidesOwnChopper;

            _seen.Clear();
            foreach (var (weapon, config, pose, entity) in SystemAPI
                         .Query<RefRO<HitscanWeaponState>, RefRO<HitscanWeaponConfig>, RefRO<LocalToWorld>>()
                         .WithEntityAccess())
            {
                // Own gun from the cockpit: no muzzle flash (the pilot sits right above it), bullets start ahead.
                var own = SystemAPI.HasComponent<GhostOwnerIsLocal>(entity) && SystemAPI.IsComponentEnabled<GhostOwnerIsLocal>(entity);
                var fromCockpit = cockpitView && own;
                var velocity = Ballistics.IsInstant(config.ValueRO.BulletSpeed)
                    ? float3.zero
                    : ObservedVelocity.Of(entity, SystemAPI.GetComponentLookup<ObservedVelocity>(true), SystemAPI.GetComponentLookup<PhysicsVelocity>(true));
                DrawShots(settings, audio, gunSound, world, now, entity, weapon.ValueRO.ShotCount, config.ValueRO,
                    math.transform(pose.ValueRO.Value, config.ValueRO.MuzzleOffset), pose.ValueRO.Forward, pose.ValueRO.Up,
                    velocity, fromCockpit, own);
            }

            // Ground turrets: the same shots, along the replicated barrel aim (the turret does not move).
            foreach (var (turret, config, pose, entity) in SystemAPI
                         .Query<RefRO<TurretState>, RefRO<TurretConfig>, RefRO<LocalToWorld>>().WithEntityAccess())
            {
                var aim = math.normalizesafe(turret.ValueRO.Aim, pose.ValueRO.Forward);
                DrawShots(settings, audio, gunSound, world, now, entity, turret.ValueRO.ShotCount, config.ValueRO.Gun,
                    TurretRules.Muzzle(pose.ValueRO.Value, config.ValueRO, aim), aim,
                    math.up(), float3.zero, fromCockpit: false, own: false);
            }

            // Swap, so despawned choppers drop out.
            _lastShotCount.Clear();
            foreach (var (entity, count) in _seen) _lastShotCount[entity] = count;
            if (_lastFlashTime.Count > _seen.Count) PruneFlashTimes();
        }

        private void DrawShots(CombatEffectsSettings settings, IAudioService audio, SoundCue gunSound, CollisionWorld world, float now,
            Entity entity, uint count, in HitscanWeaponConfig config, float3 muzzle, float3 forward, float3 up, float3 shooterVelocity,
            bool fromCockpit, bool own)
        {
            _seen[entity] = count;
            // First sight of this shooter: remember its count, draw nothing.
            if (!_lastShotCount.TryGetValue(entity, out var last) || count <= last) return;

            // The gun family's look and sound (Docs/WeaponFeelPlan.md); empty slots use the shared ones.
            var look = settings.Look(config.ModuleId, WeaponKind.Gun);
            var rotation = quaternion.LookRotationSafe(forward, up);
            if (!fromCockpit &&
                (!_lastFlashTime.TryGetValue(entity, out var lastFlash) || now - lastFlash >= settings.GunMuzzleFlashInterval))
            {
                _effects.SpawnOneShot(settings.GunMuzzleFlashFor(look), muzzle, rotation);
                _lastFlashTime[entity] = now;
            }

            audio.Play(look != null && look.FireCue != null ? look.FireCue : gunSound, muzzle);
            if (look != null && look.TailCue != null) audio.Play(look.TailCue, muzzle);

            var shots = math.min(count - last, MaxShotsPerFrame);
            // Own gun: the family's kick (small for the minigun, large for the railgun).
            if (own && look != null && look.CameraKick > 0f && _context.CameraRig != null)
                _context.CameraRig.AddShake(look.CameraKick * shots);
            var bulletSpeed = config.BulletSpeed;
            if (Ballistics.IsInstant(bulletSpeed))
            {
                // Instant gun: the hit is decided now, the visual flies at the settings' speed.
                var hit = RayCast(world, _surfaces, entity, muzzle, forward, config.Range, out var distance, out var surface);
                var start = fromCockpit ? math.min(CockpitBulletStart, distance) : 0f;
                for (var i = 0u; i < shots; i++)
                    Fire(settings, muzzle + forward * start, forward, rotation, distance - start, settings.GunBulletSpeed, hit, surface,
                        castEachFrame: false, entity, look);
            }
            else
            {
                // Round with travel time: flies as the server's round does (shooter velocity + aim × speed, for the
                // gun's flight time) and stops on whatever is in its path when it gets there.
                var launch = Ballistics.LaunchVelocity(shooterVelocity, forward, bulletSpeed);
                var speed = math.length(launch);
                var direction = math.normalizesafe(launch, forward);
                var flight = speed * Ballistics.FlightTime(config.Range, bulletSpeed);
                var start = fromCockpit ? math.min(CockpitBulletStart, flight) : 0f;
                var flightRotation = quaternion.LookRotationSafe(direction, up);
                for (var i = 0u; i < shots; i++)
                    Fire(settings, muzzle + direction * start, direction, flightRotation, flight - start, speed, false, SurfaceKind.Concrete,
                        castEachFrame: true, entity, look);
            }
        }

        private void PruneFlashTimes()
        {
            var gone = new List<Entity>();
            foreach (var entity in _lastFlashTime.Keys)
                if (!_seen.ContainsKey(entity)) gone.Add(entity);
            foreach (var entity in gone) _lastFlashTime.Remove(entity);
        }

        private void Fire(CombatEffectsSettings settings, Vector3 muzzle, Vector3 forward, Quaternion rotation, float distance, float speed,
            bool hit, SurfaceKind surface, bool castEachFrame, Entity shooter, WeaponLook look)
        {
            var visual = _effects.SpawnTracer(settings.GunBulletFor(look), muzzle, rotation, muteAudio: !settings.GunBulletAudio,
                    scale: CombatEffectsSettings.TracerScaleFor(look),
                    brightness: CombatEffectsSettings.TracerBrightnessFor(look));
            // No bullet visual set: an instant gun shows the impact at once; a round with travel time still flies unseen,
            // so its impact shows when it arrives.
            if (visual == null && !castEachFrame)
            {
                if (hit) Impact(settings, muzzle + forward * distance, forward, surface, look);
                return;
            }

            _bullets.Add(new Bullet
            {
                Visual = visual, Position = muzzle, Direction = forward, DistanceLeft = distance, Speed = speed, Impact = hit, Surface = surface,
                CastEachFrame = castEachFrame, Shooter = shooter, Look = look,
            });
        }

        /// <summary>The impact effect and the surface's hit sound. The cue's cooldown and voice limit keep fast guns tidy.</summary>
        private void Impact(CombatEffectsSettings settings, Vector3 point, Vector3 direction, SurfaceKind surface, WeaponLook look)
        {
            var sounds = _context != null ? _context.Sounds : null;
            var hit = sounds != null ? sounds.SurfaceHit(surface) : null;
            // The surface cue replaces the prefab's own sound (a fire blast per bullet, not shaped by distance or place).
            _effects.SpawnOneShot(settings.GunImpactFor(look), point, Quaternion.LookRotation(-direction), muteAudio: hit != null);
            if (hit != null) _context.Audio.Play(hit, point);
        }

        private void MoveBullets(CombatEffectsSettings settings, float deltaTime, bool hasWorld, in CollisionWorld world)
        {
            for (var i = _bullets.Count - 1; i >= 0; i--)
            {
                var bullet = _bullets[i];
                var move = Mathf.Min(bullet.Speed * deltaTime, bullet.DistanceLeft);
                if (bullet.CastEachFrame && hasWorld && move > 0f &&
                    RayCast(world, _surfaces, bullet.Shooter, bullet.Position, bullet.Direction, move, out var hitDistance, out var surface))
                {
                    Impact(settings, bullet.Position + bullet.Direction * hitDistance, bullet.Direction, surface, bullet.Look);
                    if (bullet.Visual != null) _effects.Release(bullet.Visual);
                    _bullets.RemoveAt(i);
                    continue;
                }

                bullet.Position += bullet.Direction * move;
                bullet.DistanceLeft -= move;

                if (bullet.DistanceLeft > 0f)
                {
                    if (bullet.Visual != null) bullet.Visual.transform.position = bullet.Position;
                    continue;
                }

                if (bullet.Impact)
                    Impact(settings, bullet.Position, bullet.Direction, bullet.Surface, bullet.Look);
                if (bullet.Visual != null) _effects.Release(bullet.Visual);
                _bullets.RemoveAt(i);
            }
        }

        /// <summary>Distance to the first thing the shot hits (not the shooter) and what it is made of, or the full range.</summary>
        private static bool RayCast(in CollisionWorld world, in ComponentLookup<PhysicsSurface> surfaces, Entity shooter, float3 start,
            float3 forward, float range, out float distance, out SurfaceKind surface)
        {
            var ray = new RaycastInput { Start = start, End = start + forward * range, Filter = CollisionFilter.Default };
            var hits = new NativeList<Unity.Physics.RaycastHit>(8, Allocator.Temp);
            var closest = 1f;
            var found = false;
            var nearest = default(Unity.Physics.RaycastHit);
            if (world.CastRay(ray, ref hits))
            {
                foreach (var hit in hits)
                {
                    if (hit.Entity == shooter || hit.Fraction >= closest) continue;
                    closest = hit.Fraction;
                    nearest = hit;
                    found = true;
                }
            }
            hits.Dispose();
            distance = closest * range;
            surface = found ? Surfaces.Classify(world, nearest, surfaces) : SurfaceKind.Concrete;
            return found;
        }
    }
}
