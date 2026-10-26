using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// Writes the local player's chopper values (speed, altitude, dash, health, gun aim, out of bounds) into the HUD
    /// model. Hit markers come from messages (<see cref="Feedback.CombatFeedback"/>).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class LocalChopperHudSystem : PresentationSystemBase
    {
        /// <summary>The reticle shows where the gun points at this distance (or the gun range, if shorter).</summary>
        private const float AimDistance = 200f;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            var model = context.Hud;
            var hasBounds = SystemAPI.TryGetSingleton<ArenaBounds>(out var bounds);

            foreach (var (velocity, state, config, health, localToWorld, entity) in SystemAPI
                         .Query<RefRO<PhysicsVelocity>, RefRO<ChopperFlightState>, RefRO<ChopperFlightConfig>,
                             RefRO<Health>, RefRO<LocalToWorld>>()
                         .WithAll<GhostOwnerIsLocal>()
                         .WithEntityAccess())
            {
                model.Update(
                    math.length(velocity.ValueRO.Linear),
                    state.ValueRO.Altitude,
                    state.ValueRO.DashCooldownRemaining,
                    config.ValueRO.DashCooldown);
                model.UpdateHealth(health.ValueRO.Current, health.ValueRO.Max);
                model.OutOfBounds = hasBounds && !bounds.Contains(localToWorld.ValueRO.Position);

                // The gun is a module: a loadout without one has no reticle.
                if (SystemAPI.HasComponent<HitscanWeaponConfig>(entity))
                    UpdateAim(model, SystemAPI.GetComponent<HitscanWeaponConfig>(entity), localToWorld.ValueRO);
                else
                    model.UpdateAim(new UnityEngine.Vector3(0f, 0f, -1f));
                return;
            }

            model.ClearChopper();
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
