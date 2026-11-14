using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// Client only. Fills the <see cref="WorldMarkerModel"/>: a marker on every other chopper and ground target in range
    /// (closest first, edge arrows when off screen, health bar when close), and the lead indicator for the selected
    /// rocket pylon (where to put the nose so the rockets meet the target). With rockets selected the lead is always on:
    /// with no target near the nose it shows where the rockets hit (free aim; hidden when nothing is in range). No HUD code reads ECS; this is the one place.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class WorldMarkerSystem : PresentationSystemBase
    {
        /// <summary>Targets further than this get no marker.</summary>
        private const float MarkerRange = 2000f;
        /// <summary>Health bars show only this close.</summary>
        private const float HealthBarRange = 400f;
        /// <summary>Without a lock, the lead follows the target closest to the nose inside this cone.</summary>
        private static readonly float LeadConeCos = math.cos(math.radians(15f));
        /// <summary>Ray pieces of a falling rocket's free-aim arc.</summary>
        private const int ArcPieces = 32;

        private readonly List<(float DistanceSq, WorldMarker Marker)> _candidates = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            var model = context.Markers;
            var camera = UnityEngine.Camera.main;
            if (camera == null)
            {
                model.Clear();
                return;
            }

            foreach (var (pose, velocity, targetLock, selection, self) in SystemAPI
                         .Query<RefRO<LocalToWorld>, RefRO<PhysicsVelocity>, RefRO<TargetLock>, RefRO<PylonSelection>>()
                         .WithAll<GhostOwnerIsLocal>()
                         .WithEntityAccess())
            {
                model.Begin();
                // Ping (pilot OVERWATCH): every enemy chopper at any range, with health.
                var ping = SystemAPI.HasComponent<AbilityConfig>(self) && SystemAPI.HasComponent<AbilityState>(self) &&
                           Abilities.ShowsAllEnemies(SystemAPI.GetComponent<AbilityConfig>(self), SystemAPI.GetComponent<AbilityState>(self));
                AddMarkers(model, camera, pose.ValueRO.Position, pose.ValueRO.Forward, targetLock.ValueRO.Target, ping);
                UpdateLead(model, camera, self, pose.ValueRO, velocity.ValueRO.Linear, targetLock.ValueRO, selection.ValueRO);
                model.End();
                return;
            }

            model.Clear();
        }

        private void AddMarkers(WorldMarkerModel model, UnityEngine.Camera camera, float3 position, float3 forward, Entity lockTarget, bool ping)
        {
            var heading = math.atan2(forward.x, forward.z);
            _candidates.Clear();
            foreach (var (targetable, health, pose, entity) in SystemAPI
                         .Query<RefRO<Targetable>, RefRO<Health>, RefRO<LocalToWorld>>()
                         .WithNone<GhostOwnerIsLocal>()
                         .WithEntityAccess())
            {
                if (health.ValueRO.IsDead) continue;
                var distanceSq = math.distancesq(position, pose.ValueRO.Position);
                var air = (targetable.ValueRO.Class & TargetClass.Air) != 0;
                var pinged = ping && air;
                if (!pinged && distanceSq > MarkerRange * MarkerRange) continue;

                var distance = math.sqrt(distanceSq);
                var onScreen = WorldMarkerModel.Place(camera.WorldToViewportPoint(pose.ValueRO.Position), out var viewport, out var angle);
                var max = health.ValueRO.Max;
                _candidates.Add((distanceSq, new WorldMarker
                {
                    Kind = (targetable.ValueRO.Class & TargetClass.Air) != 0 ? WorldMarkerKind.Enemy : WorldMarkerKind.Ground,
                    Viewport = viewport,
                    OnScreen = onScreen,
                    EdgeAngleDegrees = angle,
                    BearingDegrees = Bearing(pose.ValueRO.Position - position, heading),
                    Distance = (int)math.round(distance / WorldMarkerModel.DistanceStep) * WorldMarkerModel.DistanceStep,
                    HealthFraction = (pinged || distance <= HealthBarRange) && max > 0f ? math.saturate(health.ValueRO.Current / max) : -1f,
                    IsLockTarget = entity == lockTarget,
                    Pinged = pinged,
                }));
            }

            _candidates.Sort((a, b) => a.DistanceSq.CompareTo(b.DistanceSq));
            foreach (var candidate in _candidates)
                if (!model.Add(candidate.Marker)) break;
        }

        /// <summary>Clockwise from the nose in the horizontal plane, degrees -180..180.</summary>
        private static float Bearing(float3 offset, float heading)
        {
            var angle = math.degrees(math.atan2(offset.x, offset.z) - heading);
            return (angle % 360f + 540f) % 360f - 180f;
        }

        private void UpdateLead(WorldMarkerModel model, UnityEngine.Camera camera, Entity self, in LocalToWorld pose, float3 velocity,
            in TargetLock targetLock, in PylonSelection selection)
        {
            if (!SystemAPI.HasBuffer<PylonWeapon>(self)) return;
            var weapons = SystemAPI.GetBuffer<PylonWeapon>(self);
            if (selection.Selected >= weapons.Length) return;
            var weapon = weapons[selection.Selected];
            if (weapon.Behaviour != PylonBehaviour.Rocket || weapon.Speed <= 0f) return;
            // An air mine stops and hangs: it has no aim point.
            if (weapon.Drag > 0f) return;

            var muzzle = math.transform(pose.Value, weapon.MuzzleOffset);
            var range = weapon.Speed * weapon.Lifetime;
            var target = targetLock.Target != Entity.Null && SystemAPI.HasComponent<LocalToWorld>(targetLock.Target)
                ? targetLock.Target
                : TargetNearestTheNose(self, muzzle, pose.Forward, range);
            if (target != Entity.Null)
            {
                // Rockets keep the chopper's velocity, so work relative to the chopper.
                var targetPosition = SystemAPI.GetComponent<LocalToWorld>(target).Position;
                var targetVelocity = SystemAPI.HasComponent<PhysicsVelocity>(target) ? SystemAPI.GetComponent<PhysicsVelocity>(target).Linear : float3.zero;
                var relativeVelocity = targetVelocity - velocity;
                if (Guidance.InterceptTime(targetPosition - muzzle, relativeVelocity, weapon.Speed, out var time) && time <= weapon.Lifetime)
                {
                    // Gravity pulls the rocket down on the way: aim that much higher (close enough for the reticle).
                    var drop = new float3(0f, 0.5f * weapon.Gravity * time * time, 0f);
                    model.SetLead(camera.WorldToViewportPoint(targetPosition + relativeVelocity * time + drop), onTarget: true);
                    return;
                }
            }

            // Free aim: the rocket path (launch velocity as in PylonFireSystem) to the first hit. Nothing in range (open
            // sky): no reticle, so it never points at empty air.
            if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics)) return;
            var launch = velocity + pose.Forward * weapon.Speed;
            // Straight: one ray. With gravity (bombs, heavy rockets): the arc in short pieces, so it shows the impact point.
            var pieces = weapon.Gravity > 0f ? ArcPieces : 1;
            var from = muzzle;
            for (var i = 1; i <= pieces; i++)
            {
                var time = weapon.Lifetime * i / pieces;
                var to = muzzle + launch * time - new float3(0f, 0.5f * weapon.Gravity * time * time, 0f);
                if (Explosions.TryRaycast(physics.CollisionWorld, self, from, to, out var hit))
                {
                    model.SetLead(camera.WorldToViewportPoint(hit.Position), onTarget: false);
                    return;
                }
                from = to;
            }
        }

        private Entity TargetNearestTheNose(Entity self, float3 muzzle, float3 forward, float range)
        {
            var best = Entity.Null;
            var bestAlignment = LeadConeCos;
            foreach (var (health, pose, entity) in SystemAPI.Query<RefRO<Health>, RefRO<LocalToWorld>>()
                         .WithAll<Targetable>()
                         .WithEntityAccess())
            {
                if (entity == self || health.ValueRO.IsDead) continue;
                if (!Seeker.InCone(muzzle, forward, pose.ValueRO.Position, bestAlignment, range, out _, out var alignment)) continue;
                bestAlignment = alignment;
                best = entity;
            }
            return best;
        }
    }
}
