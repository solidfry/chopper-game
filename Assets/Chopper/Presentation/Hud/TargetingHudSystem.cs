using Chopper.Presentation.Bridge;
using Chopper.Presentation.Feedback;
using Chopper.Simulation.Combat;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// Client only. Writes the local player's targeting into the HUD model: the lock on the selected seeker weapon
    /// (server decided, sent to the owner), the selected pylon weapon, flares, and threats against you (enemy locks
    /// from <see cref="ThreatState"/>, the closest missile whose target is you).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class TargetingHudSystem : PresentationSystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            var model = context.Hud;
            var camera = UnityEngine.Camera.main;

            foreach (var (targetLock, selection, threat, pose, entity) in SystemAPI
                         .Query<RefRO<TargetLock>, RefRO<PylonSelection>, RefRO<ThreatState>, RefRO<LocalToWorld>>()
                         .WithAll<GhostOwnerIsLocal>()
                         .WithEntityAccess())
            {
                UpdateTargeting(model, camera, entity, targetLock.ValueRO, selection.ValueRO);
                UpdateThreats(model, camera, entity, threat.ValueRO, pose.ValueRO.Position);
                return;
            }
        }

        private void UpdateTargeting(ChopperHudModel model, UnityEngine.Camera camera, Entity self, in TargetLock targetLock,
            in PylonSelection selection)
        {
            var pylonName = "";
            var needsLock = false;
            if (SystemAPI.HasBuffer<PylonWeapon>(self))
            {
                var weapons = SystemAPI.GetBuffer<PylonWeapon>(self);
                if (selection.Selected < weapons.Length)
                {
                    var weapon = weapons[selection.Selected];
                    pylonName = weapon.Name.ToString();
                    needsLock = weapon.Behaviour == PylonBehaviour.Guided && weapon.RequiresLock;
                }
            }

            var flaresReady = SystemAPI.HasComponent<CountermeasureState>(self) &&
                              SystemAPI.GetComponent<CountermeasureState>(self).CooldownRemaining <= 0f;

            var hasTarget = targetLock.Target != Entity.Null && SystemAPI.HasComponent<LocalToWorld>(targetLock.Target) && camera != null;
            var viewport = hasTarget
                ? camera.WorldToViewportPoint(SystemAPI.GetComponent<LocalToWorld>(targetLock.Target).Position)
                : Vector3.back;
            model.UpdateTargeting(hasTarget, viewport, targetLock.Progress, targetLock.Locked, pylonName, needsLock, flaresReady);
        }

        private void UpdateThreats(ChopperHudModel model, UnityEngine.Camera camera, Entity self, in ThreatState threat, float3 position)
        {
            var closest = float.MaxValue;
            var closestPosition = float3.zero;
            foreach (var (missile, missilePose) in SystemAPI.Query<RefRO<GuidedMissile>, RefRO<LocalToWorld>>())
            {
                if (missile.ValueRO.Target != self) continue;
                var distance = math.distance(position, missilePose.ValueRO.Position);
                if (distance >= closest) continue;
                closest = distance;
                closestPosition = missilePose.ValueRO.Position;
            }

            var inbound = closest < float.MaxValue;
            var direction = inbound && camera != null ? CombatFeedback.DirectionFromCamera(camera.transform, closestPosition) : 0f;
            model.UpdateThreats(threat.Locking > 0, threat.LockedBy > 0, inbound, direction, inbound ? closest : 0f);
        }
    }
}
