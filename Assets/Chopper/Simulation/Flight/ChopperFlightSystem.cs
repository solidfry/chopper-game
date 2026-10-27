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
    /// <item>Collective (W/S) lifts along the rotor: W rises, S sinks. Hover lift holds height at any tilt.</item>
    /// <item>Tilt gives speed (<c>TiltThrust</c>): nose down goes forward, bank curves turns.</item>
    /// <item>Yaw without roll input banks the chopper into the turn (coordinated turn), blended in smoothly.</item>
    /// <item>Input sets a turn rate; rotation reaches it fast and stops when input stops.</item>
    /// <item>Drag is higher sideways and vertically than forward; the nose weathervanes into travel at speed.</item>
    /// <item>The rotor spools to the collective input (weight), and boost (held) pushes along the nose from a tank.</item>
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

                // Hover lift along the rotor axis. Tilt compensation grows it so its vertical part still holds height.
                var hoverLift = weight;
                var uprightDot = math.dot(up, worldUp);
                if (uprightDot > 0f)
                {
                    var compensation = 1f / math.max(uprightDot, config.MaxCompensatedTilt);
                    hoverLift *= math.lerp(1f, compensation, config.TiltCompensation);
                }
                var lift = up * hoverLift;
                var verticalLift = worldUp * math.dot(lift, worldUp);
                // Horizontal part of the lift = the push from tilt (TiltThrust): nose down accelerates, flaring brakes,
                // banking curves turns. PitchThrust below 1 makes the nose-axis part weaker (pitch more for aiming).
                var tiltPush = lift - verticalLift;
                var noseAxis = math.mul(rotation, math.forward());
                var flatNose = math.normalizesafe(new float3(noseAxis.x, 0f, noseAxis.z), math.forward());
                var alongNose = flatNose * math.dot(tiltPush, flatNose);
                tiltPush = (tiltPush - alongNose + alongNose * config.PitchThrust) * config.TiltThrust;
                velocity.Linear += (verticalLift + tiltPush) * DeltaTime;

                // Collective (W/S), Battlefield: extra lift along the rotor axis. Level = rise; nose down = rise and go
                // forward; S sinks. The rotor spools to the input, so lift builds and fades with a little weight.
                state.Collective += (thrust - state.Collective) * (1f - math.exp(-config.CollectiveResponse * DeltaTime));
                var spooled = state.Collective;
                var collective = spooled >= 0f ? spooled * config.CollectiveUp : spooled * config.CollectiveDown;
                velocity.Linear += up * (collective * DeltaTime);

                Boost(input, config, noseAxis, ref velocity, ref state);

                // Drag in the heading frame (yaw only: x = sideways, y = world up, z = flat nose), so sideways and
                // vertical slides stop faster than forward flight. Not the tilted body frame: there, fast forward
                // flight nose-down reads as a climb and the vertical drag pulls the chopper down. Boosting thins the
                // forward drag: a higher top speed while the tank lasts.
                var quadraticDrag = config.QuadraticDrag;
                if (state.Boosting) quadraticDrag.z *= config.BoostDragScale;
                var heading = quaternion.LookRotationSafe(flatNose, worldUp);
                var localVelocity = math.mul(math.inverse(heading), velocity.Linear);
                var dragAcceleration = -(quadraticDrag * localVelocity * math.abs(localVelocity) + config.LinearDrag * localVelocity);
                var dragDelta = dragAcceleration * DeltaTime;
                // Never let drag reverse the direction of travel within one step.
                dragDelta = math.select(dragDelta, -localVelocity, math.abs(dragDelta) > math.abs(localVelocity));
                velocity.Linear += math.mul(heading, dragDelta);

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

                // Coordinated turn: yawing (A/D) without rolling leans the chopper into the turn (up to MaxBank), so the
                // tilted lift pulls it round. Blended in smoothly (no on/off switch, which made the mouse erratic), and
                // off when there is no yaw: then the chopper keeps the bank the pilot set, like Battlefield.
                var bankAssist = math.saturate(math.abs(yaw) / 0.2f) * math.saturate(1f - math.abs(roll) / 0.3f);
                if (config.MaxBank > 0f && bankAssist > 0f)
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
                        desiredRate.z += localAxis.z * bankError * config.BankResponse * bankAssist;
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
            }

            /// <summary>
            /// Held boost from a tank. Starts with a kick, then pushes along the nose (never down, so it does not dive into
            /// the ground; nose up climbs). Ends when released or empty; the tank refills after a short wait, and only
            /// once the button is released.
            /// </summary>
            private void Boost(in ChopperInput input, in ChopperFlightConfig config, float3 noseAxis, ref PhysicsVelocity velocity,
                ref ChopperFlightState state)
            {
                var canBoost = state.Boosting ? state.BoostUsed < 1f : state.BoostUsed <= 1f - config.BoostStartEnergy;
                var boosting = input.Boost && canBoost && config.BoostDuration > 0f;
                var direction = math.normalizesafe(new float3(noseAxis.x, math.max(noseAxis.y, 0f), noseAxis.z), noseAxis);

                if (boosting)
                {
                    if (!state.Boosting) velocity.Linear += direction * config.BoostKick;
                    velocity.Linear += direction * (config.BoostAcceleration * DeltaTime);
                    state.BoostUsed = math.min(1f, state.BoostUsed + DeltaTime / config.BoostDuration);
                    state.BoostRechargeWait = config.BoostRechargeDelay;
                }
                else if (input.Boost)
                {
                    // Held on an empty (or too low) tank: no refill until released. Else the tank refills to the start
                    // level under the held button and the boost restarts by itself (a kick every second or two).
                    state.BoostRechargeWait = config.BoostRechargeDelay;
                }
                else if (state.BoostRechargeWait > 0f)
                {
                    state.BoostRechargeWait = math.max(0f, state.BoostRechargeWait - DeltaTime);
                }
                else if (config.BoostRechargeTime > 0f)
                {
                    state.BoostUsed = math.max(0f, state.BoostUsed - DeltaTime / config.BoostRechargeTime);
                }
                state.Boosting = boosting;
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
