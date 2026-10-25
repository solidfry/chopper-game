using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// Turns <see cref="ChopperInput"/> into forces just before the physics step. Netcode runs the physics group
    /// in the predicted fixed step, so this runs on the server and on the owning client (prediction) alike.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(PhysicsSystemGroup))]
    [UpdateBefore(typeof(PhysicsInitializeGroup))]
    public partial struct ChopperFlightSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new FlightJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(Simulate))]
        private partial struct FlightJob : IJobEntity
        {
            private const float MaxAltitudeProbe = 1000f;

            public float DeltaTime;
            [ReadOnly] public CollisionWorld CollisionWorld;

            private void Execute(
                Entity entity,
                in ChopperInput input,
                in ChopperFlightConfig config,
                in LocalTransform transform,
                in PhysicsMass mass,
                ref PhysicsVelocity velocity,
                ref PhysicsGravityFactor gravity,
                ref ChopperFlightState state)
            {
                var rotation = transform.Rotation;
                var up = math.mul(rotation, math.up());
                var forward = math.mul(rotation, math.forward());
                var worldUp = math.up();
                var toInertiaSpace = math.inverse(mass.InertiaOrientation);

                // Gravity is only on near the ground (landing) or when upside down; otherwise the rotor holds altitude.
                state.Altitude = MeasureAltitude(entity, transform.Position);
                var uprightDot = math.dot(up, worldUp);
                gravity.Value = state.Altitude < config.GravityAltitude || uprightDot < config.GravityUprightThreshold ? 1f : 0f;

                // Thrust: forward-tilted lift when climbing, straight down when descending.
                var thrust = math.clamp(input.Thrust, -1f, 1f);
                var force = float3.zero;
                if (thrust > 0.1f)
                    force = (up + forward * config.ThrustForwardOffset) * (config.ThrustForce * thrust);
                else if (thrust < -0.1f)
                    force = worldUp * (config.ThrustForce * thrust);
                velocity.ApplyLinearImpulse(mass, force * DeltaTime);

                // Pilot torque in local space (x = pitch, y = yaw, z = roll).
                var localTorque = new float3(
                    math.clamp(input.Pitch, -1f, 1f) * config.PitchTorque,
                    math.clamp(input.Yaw, -1f, 1f) * config.YawTorque,
                    -math.clamp(input.Roll, -1f, 1f) * config.RollTorque);
                velocity.ApplyAngularImpulse(mass, math.mul(toInertiaSpace, localTorque * DeltaTime));

                // Stabiliser: at low speed, level roll and pitch back towards upright. Yaw is left to the pilot.
                state.StabiliserActive = math.length(velocity.Linear) <= config.StabiliserMaxSpeed;
                if (state.StabiliserActive)
                {
                    var tiltAxis = math.cross(up, worldUp);
                    var sinTilt = math.length(tiltAxis);
                    if (sinTilt > 1e-4f)
                    {
                        var tiltAngle = math.atan2(sinTilt, uprightDot);
                        var localAxis = math.mul(math.inverse(rotation), tiltAxis / sinTilt);
                        localAxis.y = 0f;
                        var angularAcceleration = localAxis * (tiltAngle * config.StabiliserStrength);
                        velocity.Angular += math.mul(toInertiaSpace, angularAcceleration * DeltaTime);
                    }
                }

                // Dash: boosts current velocity, then waits for the cooldown.
                state.DashCooldownRemaining = math.max(0f, state.DashCooldownRemaining - DeltaTime);
                if (input.Dash.IsSet && state.DashCooldownRemaining <= 0f)
                {
                    velocity.Linear += velocity.Linear * config.DashBoost;
                    state.DashCooldownRemaining = config.DashCooldown;
                }
            }

            private float MeasureAltitude(Entity self, float3 position)
            {
                var ray = new RaycastInput
                {
                    Start = position,
                    End = position - new float3(0f, MaxAltitudeProbe, 0f),
                    Filter = CollisionFilter.Default,
                };

                var hits = new NativeList<RaycastHit>(8, Allocator.Temp);
                var altitude = MaxAltitudeProbe;
                if (CollisionWorld.CastRay(ray, ref hits))
                {
                    foreach (var hit in hits)
                    {
                        if (hit.Entity == self) continue;
                        altitude = math.min(altitude, hit.Fraction * MaxAltitudeProbe);
                    }
                }
                hits.Dispose();
                return altitude;
            }
        }
    }
}
