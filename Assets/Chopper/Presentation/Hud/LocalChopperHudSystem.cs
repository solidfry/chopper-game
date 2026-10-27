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
    /// Writes the local player's chopper values (speed, altitude, dash, health, attitude, gun aim, out of bounds) into the HUD
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

            foreach (var (velocity, mass, state, config, health, localToWorld, entity) in SystemAPI
                         .Query<RefRO<PhysicsVelocity>, RefRO<PhysicsMass>, RefRO<ChopperFlightState>, RefRO<ChopperFlightConfig>,
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
                UpdateAttitude(model, localToWorld.ValueRO, velocity.ValueRO, mass.ValueRO);

                // The gun is a module: a loadout without one (range 0) has no reticle.
                var gun = SystemAPI.HasComponent<HitscanWeaponConfig>(entity) ? SystemAPI.GetComponent<HitscanWeaponConfig>(entity) : default;
                if (gun.Range > 0f)
                    UpdateAim(model, gun, localToWorld.ValueRO);
                else
                    model.UpdateAim(new UnityEngine.Vector3(0f, 0f, -1f));
                return;
            }

            model.ClearChopper();
        }

        /// <summary>Bank angle for the horizon line, and turn rates (chopper axes) for the frame sway.</summary>
        private static void UpdateAttitude(ChopperHudModel model, in LocalToWorld pose, in PhysicsVelocity velocity, in PhysicsMass mass)
        {
            var worldUp = math.up();
            var roll = math.degrees(math.atan2(math.dot(pose.Right, worldUp), math.dot(pose.Up, worldUp)));
            // Same conversion as the flight model: angular velocity is stored in inertia space.
            var localRate = math.degrees(math.mul(mass.InertiaOrientation, velocity.Angular));
            model.UpdateAttitude(roll, yawRate: localRate.y, pitchRate: localRate.x);
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
