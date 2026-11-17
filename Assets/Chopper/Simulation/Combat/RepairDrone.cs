using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    public enum DronePhase : byte
    {
        /// <summary>On the pylon: can be sent (when the pylon has a round).</summary>
        Docked = 0,
        /// <summary>Flying to the target.</summary>
        Outbound = 1,
        /// <summary>At the target, mending its hull. Damage does not stop it (the field repair's trade).</summary>
        Healing = 2,
        /// <summary>Flying back; docks when it arrives.</summary>
        Returning = 3,
    }

    /// <summary>
    /// The repair drone of one chopper (owner, 17 Nov). It sits on a pylon (<see cref="PylonBehaviour.Drone"/>): pylon fire
    /// sends it to <see cref="Target"/>, it heals for a few seconds and comes back; the pylon's magazine and reload are the
    /// drone's cooldown. Only the server writes it (<see cref="RepairDroneSystem"/>); it is sent to everyone so all clients
    /// draw the drone (Presentation <c>RepairDroneEffectsSystem</c>).
    /// </summary>
    public struct RepairDroneState : IComponentData
    {
        /// <summary>A <see cref="DronePhase"/>.</summary>
        [GhostField] public byte Phase;
        /// <summary>The chopper it mends. Now always its own chopper; allies when teams exist (<see cref="RepairDrone.PickTarget"/>).</summary>
        [GhostField] public Entity Target;
        /// <summary>Seconds in the current phase.</summary>
        [GhostField(Quantization = 100)] public float PhaseTime;
        /// <summary>Seconds the current phase lasts (flight time, or the heal time).</summary>
        [GhostField(Quantization = 100)] public float PhaseLength;

        // Server only (not sent).
        public float HealRate;
        public float HealSeconds;
        public float Speed;
        /// <summary>Health mended on this trip, for the match log.</summary>
        public float Healed;

        public DronePhase State => (DronePhase)Phase;
        public bool Out => State != DronePhase.Docked;
    }

    /// <summary>The repair drone rules, pure (edit-mode tests: RepairDroneTests).</summary>
    public static class RepairDrone
    {
        /// <summary>Shortest flight (s): the drone visibly leaves and docks even when the target is its own chopper.</summary>
        public const float MinFlightSeconds = 0.8f;

        /// <summary>
        /// Who the drone goes to. No teams yet (every match is free for all), so only its own chopper. With teams: the
        /// ally under the reticle in range, else itself.
        /// </summary>
        public static Entity PickTarget(Entity self) => self;

        /// <summary>Docked, and the target lives and is hurt: a drone is never wasted on a full hull.</summary>
        public static bool CanLaunch(in RepairDroneState drone, in Health target) =>
            drone.State == DronePhase.Docked && !target.IsDead && target.Current < target.Max;

        public static float FlightSeconds(float distance, float speed) =>
            math.max(MinFlightSeconds, speed > 0f ? distance / speed : 0f);

        /// <summary>Sends the drone. Heal rate: <paramref name="healShare"/> of the target's max health over <paramref name="healSeconds"/>.</summary>
        public static void Launch(ref RepairDroneState drone, Entity target, float distance, float speed, float healShare,
            float healSeconds, float targetMax)
        {
            drone.Phase = (byte)DronePhase.Outbound;
            drone.Target = target;
            drone.PhaseTime = 0f;
            drone.Speed = speed;
            drone.PhaseLength = FlightSeconds(distance, speed);
            drone.HealSeconds = math.max(0.1f, healSeconds);
            drone.HealRate = math.saturate(healShare) * targetMax / drone.HealSeconds;
            drone.Healed = 0f;
        }

        /// <summary>
        /// One tick. <paramref name="targetValid"/> false (gone, dead) sends it home. <paramref name="distance"/> is from the
        /// owner to the target (the flight back). Returns the health added to <paramref name="target"/>.
        /// </summary>
        public static float Step(ref RepairDroneState drone, ref Health target, bool targetValid, float distance, float deltaTime)
        {
            if (drone.State == DronePhase.Docked) return 0f;
            drone.PhaseTime += deltaTime;
            switch (drone.State)
            {
                case DronePhase.Outbound:
                    if (!targetValid || target.IsDead) Return(ref drone, distance);
                    else if (drone.PhaseTime >= drone.PhaseLength) Enter(ref drone, DronePhase.Healing, drone.HealSeconds);
                    return 0f;
                case DronePhase.Healing:
                {
                    if (!targetValid || target.IsDead)
                    {
                        Return(ref drone, distance);
                        return 0f;
                    }
                    var before = target.Current;
                    target.Current = math.min(target.Max, target.Current + drone.HealRate * deltaTime);
                    var added = target.Current - before;
                    drone.Healed += added;
                    if (drone.PhaseTime >= drone.PhaseLength || target.Current >= target.Max) Return(ref drone, distance);
                    return added;
                }
                case DronePhase.Returning:
                    if (drone.PhaseTime >= drone.PhaseLength) Enter(ref drone, DronePhase.Docked, 0f);
                    return 0f;
            }
            return 0f;
        }

        /// <summary>
        /// The heal look (sheen, motes) shows: the field repair runs, or its drone heals it. Drones only heal their own
        /// chopper now; with allies, the client will also need the drones that target this chopper.
        /// </summary>
        public static bool IsBeingHealed(Entity chopper, in ComponentLookup<ChopperRepair> repairs, in ComponentLookup<RepairDroneState> drones)
        {
            if (repairs.TryGetComponent(chopper, out var repair) && repair.State == RepairPhase.Repairing) return true;
            return drones.TryGetComponent(chopper, out var drone) && drone.State == DronePhase.Healing && drone.Target == chopper;
        }

        /// <summary>The owner died or respawned: the drone is lost and the next chopper starts with it docked.</summary>
        public static void Reset(ref RepairDroneState drone) => drone = default;

        private static void Return(ref RepairDroneState drone, float distance) =>
            Enter(ref drone, DronePhase.Returning, FlightSeconds(distance, drone.Speed));

        private static void Enter(ref RepairDroneState drone, DronePhase phase, float length)
        {
            drone.Phase = (byte)phase;
            drone.PhaseTime = 0f;
            drone.PhaseLength = length;
        }
    }
}
