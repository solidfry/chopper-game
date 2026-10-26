using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// Writes the local player's chopper values (speed, altitude, dash, health, gun aim, out of bounds) into the scene's HUD model,
    /// and turns server hit confirms into hit markers.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class LocalChopperHudSystem : SystemBase
    {
        /// <summary>The reticle shows where the gun points at this distance (or the gun range, if shorter).</summary>
        private const float AimDistance = 200f;

        private ChopperHudModelHost _host;
        private EntityQuery _hitConfirms;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
            _hitConfirms = SystemAPI.QueryBuilder().WithAll<HitConfirmRpc, ReceiveRpcCommandRequest>().Build();
        }

        protected override void OnUpdate()
        {
            if (_host == null)
                _host = Object.FindFirstObjectByType<ChopperHudModelHost>();

            var model = _host != null ? _host.Model : null;
            ConsumeHitConfirms(model);
            if (model == null) return;

            model.Tick(UnityEngine.Time.deltaTime);
            var hasBounds = SystemAPI.TryGetSingleton<ArenaBounds>(out var bounds);

            foreach (var (velocity, state, config, health, gun, localToWorld) in SystemAPI
                         .Query<RefRO<PhysicsVelocity>, RefRO<ChopperFlightState>, RefRO<ChopperFlightConfig>,
                             RefRO<Health>, RefRO<HitscanWeaponConfig>, RefRO<LocalToWorld>>()
                         .WithAll<GhostOwnerIsLocal>())
            {
                model.Update(
                    math.length(velocity.ValueRO.Linear),
                    state.ValueRO.Altitude,
                    state.ValueRO.DashCooldownRemaining,
                    config.ValueRO.DashCooldown);
                model.UpdateHealth(health.ValueRO.Current, health.ValueRO.Max);
                UpdateAim(model, gun.ValueRO, localToWorld.ValueRO);
                model.OutOfBounds = hasBounds && !bounds.Contains(localToWorld.ValueRO.Position);
                return;
            }

            model.ClearChopper();
        }

        /// <summary>Always destroys the RPC entities, also when there is no HUD, so they do not pile up.</summary>
        private void ConsumeHitConfirms(ChopperHudModel model)
        {
            if (_hitConfirms.IsEmpty) return;

            if (model != null)
            {
                foreach (var confirm in SystemAPI.Query<RefRO<HitConfirmRpc>>().WithAll<ReceiveRpcCommandRequest>())
                    model.ShowHit(confirm.ValueRO.Killed);
            }
            EntityManager.DestroyEntity(_hitConfirms);
        }

        private static void UpdateAim(ChopperHudModel model, in HitscanWeaponConfig gun, in LocalToWorld pose)
        {
            var camera = UnityEngine.Camera.main;
            if (camera == null) return;

            // Rendered pose (interpolated), so the reticle does not jitter against the chopper.
            var muzzle = math.transform(pose.Value, gun.MuzzleOffset);
            var aimPoint = muzzle + pose.Forward * math.min(AimDistance, gun.Range);
            model.UpdateAim(camera.WorldToViewportPoint(aimPoint));
        }
    }
}
