using System;
using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Events;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Visuals;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Draws rockets from <see cref="RocketLaunched"/> messages: muzzle flash, the rocket visual in flight,
    /// and an impact where it hits. Visual only: the client flies the same path (<see cref="RocketFlight"/>) and
    /// ray-casts its own world to know where to stop (contact, or the proximity fuse); the server decides damage.
    /// Prefabs come from the presentation context.
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
            public float Gravity;
            public float Drag;
            public float ProximityFuse;
            public float SplashRadius;
            public TargetClass FuseTargets;
            /// <summary>The pylon family's look (blast); null = shared.</summary>
            public WeaponLook Look;
            /// <summary>The look's flight loop (bomb whistle) while it flies; null = none.</summary>
            public Chopper.Presentation.Audio.SoundLoop Loop;
            /// <summary>The arm chirp has played (an air mine that has stopped).</summary>
            public bool Armed;
        }

        private struct FuseTarget
        {
            public float3 Position;
            public TargetClass Class;
            public int OwnerNetworkId;
        }

        private readonly List<FuseTarget> _fuseTargets = new();

        private readonly List<RocketVisual> _rockets = new();
        private readonly List<RocketLaunched> _launches = new();
        private PresentationContext _context;
        private IDisposable _subscription;
        /// <summary>This frame's map dust colour (sand or ash) for blasts on the ground.</summary>
        private Color _mapDust;

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
            var surfaces = SystemAPI.GetComponentLookup<PhysicsSurface>(isReadOnly: true);
            CollectFuseTargets(owners);
            var hasAtmosphere = SystemAPI.TryGetSingleton<CityAtmosphere>(out var atmosphere);
            _mapDust = DamageParticles.MapDust(effects.Settings, hasAtmosphere, atmosphere.Colour);
            MoveRockets(effects, physics.CollisionWorld, owners, surfaces, UnityEngine.Time.deltaTime, context.HeatMarks, context);
        }

        /// <summary>Subscribes to the context's launches; a new context (scene change) drops the old rockets.</summary>
        private void Bind(PresentationContext context)
        {
            if (_context == context) return;
            _subscription?.Dispose();
            foreach (var rocket in _rockets)
                if (rocket.Loop != null) _context?.Audio?.StopLoop(rocket.Loop);
            _rockets.Clear();
            _launches.Clear();
            _context = context;
            // Messages arrive earlier in the frame (PresentationEventSystem); spawn them in OnUpdate.
            _subscription = context.Events.Subscribe<RocketLaunched>(_launches.Add);
        }

        private void Spawn(CombatEffects effects, in RocketLaunched launch)
        {
            var settings = effects.Settings;
            var rotation = Quaternion.LookRotation(launch.Velocity);
            if (launch.Submunition)
            {
                SpawnBomblet(effects, launch, rotation);
                return;
            }
            var look = settings.Look(launch.ModuleId, WeaponLookLibrary.RocketKind(launch.Gravity, launch.Drag));
            // A look with its own launch or flight sound mutes the pack's per-round sounds (flash and projectile).
            var ownSound = look != null && look.OwnsRoundSound;
            effects.SpawnOneShot(settings.RocketMuzzleFlashFor(look), launch.Position, rotation, muteAudio: ownSound);
            if (look != null && look.FireCue != null) _context?.Audio?.Play(look.FireCue, launch.Position);
            var visual = effects.SpawnPooled(settings.RocketFor(look), launch.Position, rotation, muteAudio: ownSound);
            // The shared rocket model serves every type: its size follows the blast, so a bomb reads bigger than a light
            // rocket. A family with a true-size model (bomb, mine) keeps its size. Set on every spawn: pooled.
            var scale = look == null || look.ScaleWithBlast ? RocketScale(launch.SplashRadius) : 1f;
            if (visual != null) visual.transform.localScale = Vector3.one * scale;
            var loop = look != null && look.FlightLoop != null ? _context?.Audio?.StartLoop(look.FlightLoop, launch.Position) : null;
            _rockets.Add(new RocketVisual
            {
                GameObject = visual,
                Position = launch.Position,
                Velocity = launch.Velocity,
                Remaining = launch.Lifetime,
                ShooterNetworkId = launch.ShooterNetworkId,
                Gravity = launch.Gravity,
                Drag = launch.Drag,
                ProximityFuse = launch.ProximityFuse,
                SplashRadius = launch.SplashRadius,
                FuseTargets = launch.FuseTargets,
                Look = look,
                Loop = loop,
            });
        }

        /// <summary>
        /// A cluster bomblet: the carrier family's bomblet model (else the shared rocket), no launch flash; the first of
        /// a split plays the split flash and sound once. The blast is the shared rocket blast.
        /// </summary>
        private void SpawnBomblet(CombatEffects effects, in RocketLaunched launch, Quaternion rotation)
        {
            var settings = effects.Settings;
            var carrier = settings.Look(launch.ModuleId, WeaponKind.Guided);
            if (launch.FirstOfSplit && carrier != null)
            {
                effects.SpawnOneShot(carrier.SplitFlash, launch.Position, rotation);
                if (carrier.SplitCue != null) _context?.Audio?.Play(carrier.SplitCue, launch.Position);
            }
            var model = carrier != null ? carrier.Bomblet : null;
            var visual = effects.SpawnPooled(model != null ? model : settings.Rocket, launch.Position, rotation);
            if (visual != null) visual.transform.localScale = Vector3.one * (model != null ? 1f : RocketScale(launch.SplashRadius));
            _rockets.Add(new RocketVisual
            {
                GameObject = visual,
                Position = launch.Position,
                Velocity = launch.Velocity,
                Remaining = launch.Lifetime,
                ShooterNetworkId = launch.ShooterNetworkId,
            });
        }

        /// <summary>Model scale for a blast radius: the standard rocket pod (8 m) = 1.</summary>
        public static float RocketScale(float splashRadius) =>
            splashRadius > 0f ? Mathf.Clamp(splashRadius / StandardSplashRadius, 0.6f, 2.2f) : 1f;

        private const float StandardSplashRadius = 8f;

        /// <summary>Live targets for the proximity fuses in flight (none when no rocket has a fuse).</summary>
        private void CollectFuseTargets(ComponentLookup<GhostOwner> owners)
        {
            _fuseTargets.Clear();
            var needed = false;
            foreach (var rocket in _rockets) needed |= rocket.ProximityFuse > 0f;
            if (!needed) return;

            foreach (var (targetable, health, pose, entity) in SystemAPI
                         .Query<RefRO<Targetable>, RefRO<Health>, RefRO<LocalToWorld>>()
                         .WithEntityAccess())
            {
                if (health.ValueRO.IsDead) continue;
                _fuseTargets.Add(new FuseTarget
                {
                    Position = pose.ValueRO.Position,
                    Class = targetable.ValueRO.Class,
                    // 0 = no owner (a ground target): never the shooter.
                    OwnerNetworkId = owners.TryGetComponent(entity, out var owner) ? owner.NetworkId : 0,
                });
            }
        }

        private bool TryFuse(RocketVisual rocket, float3 end, out float3 burst)
        {
            burst = end;
            if (rocket.ProximityFuse <= 0f) return false;
            foreach (var target in _fuseTargets)
            {
                if ((target.Class & rocket.FuseTargets) == 0) continue;
                if (target.OwnerNetworkId != 0 && target.OwnerNetworkId == rocket.ShooterNetworkId) continue;
                if (RocketFlight.FuseHit(rocket.Position, end, target.Position, rocket.ProximityFuse, out burst)) return true;
            }
            return false;
        }

        private void MoveRockets(CombatEffects effects, in CollisionWorld world, ComponentLookup<GhostOwner> owners,
            ComponentLookup<PhysicsSurface> surfaces, float deltaTime,
            Chopper.Presentation.Camera.HeatMarks heat, PresentationContext context)
        {
            for (var i = _rockets.Count - 1; i >= 0; i--)
            {
                var rocket = _rockets[i];
                var end = RocketFlight.Step(rocket.Position, ref rocket.Velocity, rocket.Gravity, rocket.Drag, deltaTime);
                // A mine that has stopped has no heading: keep the effect upright.
                var back = math.lengthsq(rocket.Velocity) > 1e-4f ? Quaternion.LookRotation(-rocket.Velocity) : Quaternion.identity;

                var struck = TryHit(world, owners, surfaces, rocket.ShooterNetworkId, rocket.Position, end, out var impact, out var normal,
                    out var surface);
                if (struck || TryFuse(rocket, end, out impact))
                {
                    // A blast on a surface throws it up (dirt, concrete dust, sparks, water); an air burst throws nothing.
                    if (struck)
                        effects.Particles.EmitSurfaceBlast(impact, normal, SurfaceBlast.For(surface, rocket.SplashRadius), _mapDust);
                    // The blast sound comes from the cue (place rules: far and hidden blasts are duller), not the prefab.
                    var blast = context.Sounds != null ? context.Sounds.RocketBlast : null;
                    effects.SpawnOneShot(effects.Settings.RocketImpactFor(rocket.Look), impact, back, muteAudio: blast != null);
                    if (rocket.Look != null) effects.SpawnLayers(rocket.Look.ImpactLayers, impact, muteAudio: true);
                    effects.SpawnShock(impact, rocket.SplashRadius);
                    if (blast != null) context.Audio?.Play(blast, (Vector3)impact);
                    heat.AddTimed(impact, VisionMath.BlastHeat, VisionMath.BlastSeconds);
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
                heat.AddFrame(end, VisionMath.RocketHeat, math.length(rocket.Velocity));
                rocket.Loop?.Set(end, 1f, 1f);
                // An air mine that has stopped is armed: one chirp.
                if (!rocket.Armed && rocket.Look != null && rocket.Look.ArmCue != null &&
                    RocketFlight.HasSettled(rocket.Drag, math.length(rocket.Velocity)))
                {
                    rocket.Armed = true;
                    context.Audio?.Play(rocket.Look.ArmCue, (Vector3)end);
                }
                if (rocket.GameObject != null)
                {
                    rocket.GameObject.transform.position = end;
                    // A bent path: the nose follows the velocity.
                    if (RocketFlight.IsBallistic(rocket.Gravity, rocket.Drag) && math.lengthsq(rocket.Velocity) > 1f)
                        rocket.GameObject.transform.rotation = Quaternion.LookRotation(rocket.Velocity);
                }
            }
        }

        /// <summary>Stops the rocket; its smoke trail fades before the pool takes it back.</summary>
        private void Finish(CombatEffects effects, int index, float3 position)
        {
            var visual = _rockets[index].GameObject;
            var loop = _rockets[index].Loop;
            if (loop != null) _context?.Audio?.StopLoop(loop);
            if (visual != null)
            {
                visual.transform.position = position;
                effects.Release(visual, effects.Settings.RocketTrailLinger);
            }
            _rockets.RemoveAt(index);
        }

        /// <summary>The nearest hit on the step that is not the shooter, with its surface normal and what it is made of.</summary>
        private static bool TryHit(in CollisionWorld world, ComponentLookup<GhostOwner> owners, in ComponentLookup<PhysicsSurface> surfaces,
            int shooterNetworkId, float3 start, float3 end, out float3 impact, out float3 normal, out SurfaceKind surface)
        {
            var ray = new RaycastInput { Start = start, End = end, Filter = CollisionFilter.Default };
            var hits = new NativeList<Unity.Physics.RaycastHit>(8, Allocator.Temp);
            var closest = float.MaxValue;
            impact = end;
            normal = math.up();
            surface = SurfaceKind.Concrete;
            var nearest = default(Unity.Physics.RaycastHit);

            if (world.CastRay(ray, ref hits))
            {
                foreach (var hit in hits)
                {
                    if (hit.Fraction >= closest) continue;
                    if (owners.TryGetComponent(hit.Entity, out var owner) && owner.NetworkId == shooterNetworkId) continue;
                    closest = hit.Fraction;
                    impact = hit.Position;
                    normal = hit.SurfaceNormal;
                    nearest = hit;
                }
            }
            hits.Dispose();
            if (closest == float.MaxValue) return false;
            surface = Surfaces.Classify(world, nearest, surfaces);
            return true;
        }
    }
}
