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
    /// rocket pylon (where to put the nose so the rockets meet the target). No HUD code reads ECS; this is the one place.
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
                AddMarkers(model, camera, pose.ValueRO.Position, targetLock.ValueRO.Target);
                UpdateLead(model, camera, self, pose.ValueRO, velocity.ValueRO.Linear, targetLock.ValueRO, selection.ValueRO);
                model.End();
                return;
            }

            model.Clear();
        }

        private void AddMarkers(WorldMarkerModel model, UnityEngine.Camera camera, float3 position, Entity lockTarget)
        {
            _candidates.Clear();
            foreach (var (targetable, health, pose, entity) in SystemAPI
                         .Query<RefRO<Targetable>, RefRO<Health>, RefRO<LocalToWorld>>()
                         .WithNone<GhostOwnerIsLocal>()
                         .WithEntityAccess())
            {
                if (health.ValueRO.IsDead) continue;
                var distanceSq = math.distancesq(position, pose.ValueRO.Position);
                if (distanceSq > MarkerRange * MarkerRange) continue;

                var distance = math.sqrt(distanceSq);
                var onScreen = WorldMarkerModel.Place(camera.WorldToViewportPoint(pose.ValueRO.Position), out var viewport, out var angle);
                var max = health.ValueRO.Max;
                _candidates.Add((distanceSq, new WorldMarker
                {
                    Kind = (targetable.ValueRO.Class & TargetClass.Air) != 0 ? WorldMarkerKind.Enemy : WorldMarkerKind.Ground,
                    Viewport = viewport,
                    OnScreen = onScreen,
                    EdgeAngleDegrees = angle,
                    Distance = (int)math.round(distance / WorldMarkerModel.DistanceStep) * WorldMarkerModel.DistanceStep,
                    HealthFraction = distance <= HealthBarRange && max > 0f ? math.saturate(health.ValueRO.Current / max) : -1f,
                    IsLockTarget = entity == lockTarget,
                }));
            }

            _candidates.Sort((a, b) => a.DistanceSq.CompareTo(b.DistanceSq));
            foreach (var candidate in _candidates)
                if (!model.Add(candidate.Marker)) break;
        }

        private void UpdateLead(WorldMarkerModel model, UnityEngine.Camera camera, Entity self, in LocalToWorld pose, float3 velocity,
            in TargetLock targetLock, in PylonSelection selection)
        {
            if (!SystemAPI.HasBuffer<PylonWeapon>(self)) return;
            var weapons = SystemAPI.GetBuffer<PylonWeapon>(self);
            if (selection.Selected >= weapons.Length) return;
            var weapon = weapons[selection.Selected];
            if (weapon.Behaviour != PylonBehaviour.Rocket || weapon.Speed <= 0f) return;

            var muzzle = math.transform(pose.Value, weapon.MuzzleOffset);
            var range = weapon.Speed * weapon.Lifetime;
            var target = targetLock.Target != Entity.Null && SystemAPI.HasComponent<LocalToWorld>(targetLock.Target)
                ? targetLock.Target
                : TargetNearestTheNose(self, muzzle, pose.Forward, range);
            if (target == Entity.Null) return;

            // Rockets keep the chopper's velocity, so work relative to the chopper.
            var targetPosition = SystemAPI.GetComponent<LocalToWorld>(target).Position;
            var targetVelocity = SystemAPI.HasComponent<PhysicsVelocity>(target) ? SystemAPI.GetComponent<PhysicsVelocity>(target).Linear : float3.zero;
            var relativeVelocity = targetVelocity - velocity;
            if (!Guidance.InterceptTime(targetPosition - muzzle, relativeVelocity, weapon.Speed, out var time)) return;
            if (time > weapon.Lifetime) return;

            model.SetLead(camera.WorldToViewportPoint(targetPosition + relativeVelocity * time));
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
