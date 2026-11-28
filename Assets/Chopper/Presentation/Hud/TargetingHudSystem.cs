using Chopper.Presentation.Bridge;
using Chopper.Presentation.Feedback;
using Chopper.Simulation.Combat;
using Unity.Collections;
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
        // The selected pylon's name as text, made again only when the name changes (no string per frame).
        private FixedString32Bytes _pylonName;
        private byte _pylonSlot = byte.MaxValue;
        private string _pylonText = "";

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
            var pylonRounds = 0;
            var pylonMagazine = 0;
            var pylonReload = 1f;
            var reticle = ReticleKind.Gun;
            if (SystemAPI.HasBuffer<PylonWeapon>(self))
            {
                var weapons = SystemAPI.GetBuffer<PylonWeapon>(self);
                var pylonStates = SystemAPI.GetBuffer<PylonWeaponState>(self);
                if (selection.Selected < weapons.Length && selection.Selected < pylonStates.Length)
                {
                    var weapon = weapons[selection.Selected];
                    pylonMagazine = weapon.MagazineSize;
                    var pylonState = pylonStates[selection.Selected];
                    pylonRounds = Magazine.RoundsLeft(pylonState.RoundsUsed, weapon.MagazineSize);
                    pylonReload = Magazine.ReloadProgress(pylonState.RoundsUsed, pylonState.ReloadRemaining, weapon.ReloadSeconds);
                    // "2 HOMING MISSILES": the number is the key that picks it (1-5).
                    if (weapon.Name != _pylonName || selection.Selected != _pylonSlot)
                    {
                        _pylonName = weapon.Name;
                        _pylonSlot = selection.Selected;
                        _pylonText = weapons.Length > 1 ? $"{selection.Selected + 1} {weapon.Name}" : weapon.Name.ToString();
                    }
                    reticle = Reticles.Of(weapon);
                    pylonName = _pylonText;
                    needsLock = weapon.Behaviour == PylonBehaviour.Guided && weapon.RequiresLock;
                }
            }

            var flaresReady = false;
            var flareRounds = 0;
            var flareMagazine = -1;
            var flareReload = 1f;
            if (SystemAPI.HasComponent<CountermeasureConfig>(self))
            {
                var config = SystemAPI.GetComponent<CountermeasureConfig>(self);
                var flares = SystemAPI.GetComponent<CountermeasureState>(self);
                // Cooldown 0 = the loadout has no flare module (flares are a utility choice): the HUD says NO FLARES.
                flareMagazine = config.Cooldown > 0f ? config.MagazineSize : -1;
                flareRounds = Magazine.RoundsLeft(flares.RoundsUsed, config.MagazineSize);
                flareReload = Magazine.ReloadProgress(flares.RoundsUsed, flares.ReloadRemaining, config.ReloadSeconds);
                flaresReady = config.Cooldown > 0f && flares.CooldownRemaining <= 0f && Magazine.HasRound(flares.RoundsUsed, config.MagazineSize);
            }

            var gunHasHeat = false;
            var gunHeat = 0f;
            var gunOverheated = false;
            if (SystemAPI.HasComponent<HitscanWeaponConfig>(self))
            {
                var gun = SystemAPI.GetComponent<HitscanWeaponState>(self);
                gunHasHeat = SystemAPI.GetComponent<HitscanWeaponConfig>(self).HeatPerShot > 0f;
                gunHeat = gun.Heat;
                gunOverheated = gun.Overheated;
            }
            model.UpdateWeaponLimits(gunHasHeat, gunHeat, gunOverheated, pylonRounds, pylonMagazine, flareRounds, flareMagazine,
                pylonReload, flareReload);

            var hasTarget = targetLock.Target != Entity.Null && SystemAPI.HasComponent<LocalToWorld>(targetLock.Target) && camera != null;
            // No lock, but a target in the cone is under the lock floor: the box sits on it with a cross.
            var tooLow = !hasTarget && targetLock.LowTarget != Entity.Null && SystemAPI.HasComponent<LocalToWorld>(targetLock.LowTarget) &&
                         camera != null;
            // A seeker fooled by flares: the lock box sits on the flare cloud, not the chopper.
            float3 lockPoint = hasTarget ? SystemAPI.GetComponent<LocalToWorld>(targetLock.Target).Position
                : tooLow ? SystemAPI.GetComponent<LocalToWorld>(targetLock.LowTarget).Position : float3.zero;
            if (hasTarget && targetLock.OnFlares && SystemAPI.HasComponent<CountermeasureState>(targetLock.Target))
                lockPoint = FlareFlight.Position(SystemAPI.GetComponent<CountermeasureState>(targetLock.Target));
            var viewport = hasTarget || tooLow ? camera.WorldToViewportPoint(lockPoint) : Vector3.back;
            model.SetReticle(reticle);
            model.UpdateTargeting(hasTarget || tooLow, viewport, tooLow ? 0f : targetLock.Progress, targetLock.Locked, pylonName, needsLock,
                flaresReady, tooLow);
        }

        private void UpdateThreats(ChopperHudModel model, UnityEngine.Camera camera, Entity self, in ThreatState threat, float3 position)
        {
            var closest = float.MaxValue;
            var closestPosition = float3.zero;
            foreach (var (missile, missilePose) in SystemAPI.Query<RefRO<GuidedMissile>, RefRO<LocalToWorld>>())
            {
                // A missile fooled by flares still names its target (it loses it when they burn out): no warning.
                if (missile.ValueRO.Target != self || missile.ValueRO.Decoyed) continue;
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
