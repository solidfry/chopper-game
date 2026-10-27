using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// Battlefield-style helicopter model, applied just before the physics step. Netcode runs the physics group in
    /// the predicted fixed step, so this runs on the server and on the owning client (prediction) alike.
    /// <list type="bullet">
    /// <item>Gravity is always on. The rotor lifts along its own up axis: hover lift at zero collective.</item>
    /// <item>Tilting moves the chopper; <c>TiltThrust</c> makes a small nose-down give real speed.</item>
    /// <item>Collective (W/S) is mostly straight up: it changes height, it does not steal speed from tilt.</item>
    /// <item>Yaw without roll input banks the chopper into the turn (coordinated turn), so turns curve.</item>
    /// <item>Input sets a turn rate; rotation reaches it fast and stops when input stops.</item>
    /// <item>Drag is higher sideways and vertically than forward; the nose weathervanes into travel at speed.</item>
    /// </list>
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
            var gravity = SystemAPI.TryGetSingleton<PhysicsStep>(out var step) ? step.Gravity : PhysicsStep.Default.Gravity;
            state.Dependency = new FlightJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Gravity = math.length(gravity),
                CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(Simulate))]
        private partial struct FlightJob : IJobEntity
        {
            private const float MaxAltitudeProbe = 1000f;
            private const float NoInput = 0.05f;
            private const float WeathervaneMinSpeed = 5f;

            public float DeltaTime;
            public float Gravity;
            [ReadOnly] public CollisionWorld CollisionWorld;

            private void Execute(
                Entity entity,
                in ChopperInput input,
                in ChopperFlightConfig config,
                in LocalTransform transform,
                in PhysicsMass mass,
                ref PhysicsVelocity velocity,
                ref ChopperFlightState state)
            {
                var rotation = transform.Rotation;
                var toLocal = math.inverse(rotation);
                var up = math.mul(rotation, math.up());
                var worldUp = math.up();

                state.Altitude = MeasureAltitude(entity, transform.Position);

                var thrust = math.clamp(input.Thrust, -1f, 1f);
                var pitch = math.clamp(input.Pitch, -1f, 1f);
                var yaw = math.clamp(input.Yaw, -1f, 1f);
                var roll = math.clamp(input.Roll, -1f, 1f);

                // Extra weight on top of physics gravity.
                var weight = Gravity * config.GravityScale;
                velocity.Linear -= worldUp * (Gravity * (config.GravityScale - 1f) * DeltaTime);

                // Hover lift along the rotor axis. Tilt compensation grows it so its vertical part still holds height;
                // the horizontal part is the push from tilt (scaled by TiltThrust): tilting accelerates, flaring brakes.
                var hoverLift = weight;
                var uprightDot = math.dot(up, worldUp);
                if (uprightDot > 0f)
                {
                    var compensation = 1f / math.max(uprightDot, config.MaxCompensatedTilt);
                    hoverLift *= math.lerp(1f, compensation, config.TiltCompensation);
                }
                var lift = up * hoverLift;
                var verticalLift = worldUp * math.dot(lift, worldUp);
                velocity.Linear += (verticalLift + (lift - verticalLift) * config.TiltThrust) * DeltaTime;

                // Collective (W/S): mostly straight up, so it changes height and does not steal speed from tilt.
                var collectiveDirection = math.normalizesafe(math.lerp(up, worldUp, config.CollectiveVertical), worldUp);
                velocity.Linear += collectiveDirection * (weight * thrust * config.CollectiveRange * DeltaTime);

                // Drag in the chopper's own frame, so sideways and vertical slides stop faster than forward flight.
                var localVelocity = math.mul(toLocal, velocity.Linear);
                var dragAcceleration = -(config.QuadraticDrag * localVelocity * math.abs(localVelocity) + config.LinearDrag * localVelocity);
                var dragDelta = dragAcceleration * DeltaTime;
                // Never let drag reverse the direction of travel within one step.
                dragDelta = math.select(dragDelta, -localVelocity, math.abs(dragDelta) > math.abs(localVelocity));
                velocity.Linear += math.mul(rotation, dragDelta);

                // Requested turn rate in local space (x = pitch, y = yaw, z = roll).
                var desiredRate = config.MaxTurnRate * new float3(pitch, yaw, -roll);

                // Auto-level: with no pitch/roll input, rotate back towards upright. Yaw stays with the pilot.
                if (config.AutoLevel > 0f && math.abs(pitch) < NoInput && math.abs(roll) < NoInput)
                {
                    var tiltAxis = math.cross(up, worldUp);
                    var sinTilt = math.length(tiltAxis);
                    if (sinTilt > 1e-4f)
                    {
                        var tiltAngle = math.atan2(sinTilt, math.dot(up, worldUp));
                        var localAxis = math.mul(toLocal, tiltAxis / sinTilt);
                        desiredRate.x += localAxis.x * tiltAngle * config.AutoLevel;
                        desiredRate.z += localAxis.z * tiltAngle * config.AutoLevel;
                    }
                }

                // Coordinated turn: with no roll input, lean into the yaw (up to MaxBank at full yaw), so the tilted lift
                // pulls the chopper round the turn. Only the roll axis is driven; pitch stays with the pilot (aim).
                if (config.MaxBank > 0f && math.abs(roll) < NoInput)
                {
                    var forwardAxis = math.mul(rotation, math.forward());
                    var flatForward = math.normalizesafe(new float3(forwardAxis.x, 0f, forwardAxis.z), math.forward());
                    var flatRight = math.cross(worldUp, flatForward);
                    var bank = yaw * config.MaxBank;
                    var targetUp = worldUp * math.cos(bank) + flatRight * math.sin(bank);
                    // Compare in the plane across the nose, so pitch does not count as bank.
                    var upAcross = math.normalizesafe(up - flatForward * math.dot(up, flatForward), worldUp);
                    var bankAxis = math.cross(upAcross, targetUp);
                    var sinBank = math.length(bankAxis);
                    if (sinBank > 1e-4f)
                    {
                        var bankError = math.atan2(sinBank, math.dot(upAcross, targetUp));
                        var localAxis = math.mul(toLocal, bankAxis / sinBank);
                        desiredRate.z += localAxis.z * bankError * config.BankResponse;
                    }
                }

                // Weathervane: at speed, yaw the nose into the horizontal direction of travel.
                var horizontalVelocity = new float3(velocity.Linear.x, 0f, velocity.Linear.z);
                var horizontalSpeed = math.length(horizontalVelocity);
                if (config.Weathervane > 0f && horizontalSpeed > WeathervaneMinSpeed)
                {
                    var forward = math.mul(rotation, math.forward());
                    var flatForward = math.normalizesafe(new float3(forward.x, 0f, forward.z));
                    var travel = horizontalVelocity / horizontalSpeed;
                    var signedAngle = math.atan2(math.dot(math.cross(flatForward, travel), worldUp), math.dot(flatForward, travel));
                    // Only when flying forwards; reversing should not spin the chopper around.
                    if (math.dot(flatForward, travel) > 0f)
                        desiredRate += math.mul(toLocal, worldUp) * (signedAngle * horizontalSpeed * config.Weathervane);
                }

                // Rate controller: move the current local turn rate towards the requested rate.
                var localRate = math.mul(mass.InertiaOrientation, velocity.Angular);
                localRate += (desiredRate - localRate) * (1f - math.exp(-config.TurnResponse * DeltaTime));
                velocity.Angular = math.mul(math.inverse(mass.InertiaOrientation), localRate);

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
