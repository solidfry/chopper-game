using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Field repair of one chopper (owner, 7 Nov): hold the repair key to mend the hull, full health in
    /// <see cref="Seconds"/>. Any damage while repairing stops it and locks it for <see cref="InterruptCooldown"/>.
    /// From the chassis (<see cref="Flight.RepairTuning"/>); pilots will scale it (plan: Pilots).
    /// </summary>
    public struct RepairConfig : IComponentData
    {
        /// <summary>Seconds from 0 to full health (the rate is Max / Seconds per second).</summary>
        public float Seconds;
        /// <summary>Seconds locked after a hit stops a repair.</summary>
        public float InterruptCooldown;

        public static RepairConfig Default => new() { Seconds = 10f, InterruptCooldown = 3f };
    }

    public enum RepairPhase : byte
    {
        Idle = 0,
        Repairing = 1,
        /// <summary>A hit stopped the repair; it can start again when the cooldown ends.</summary>
        Locked = 2,
    }

    /// <summary>
    /// Repair state. Only the server writes it (<see cref="RepairSystem"/>, <see cref="ApplyDamageSystem"/>); it is sent
    /// to every client: the owner's HUD shows the progress and the lock, and everyone sees the repair sparks.
    /// </summary>
    public struct ChopperRepair : IComponentData
    {
        /// <summary>A <see cref="RepairPhase"/>.</summary>
        [GhostField] public byte Phase;
        /// <summary>Seconds of lock left (<see cref="RepairPhase.Locked"/>).</summary>
        [GhostField(Quantization = 100)] public float LockLeft;

        public RepairPhase State => (RepairPhase)Phase;
    }

    /// <summary>The repair rules, pure (edit-mode tests: RepairTests).</summary>
    public static class Repair
    {
        /// <summary>
        /// One tick. Held, alive, not full and not locked: heal at Max / Seconds per second. Released or full: idle.
        /// Locked: count down; when it ends, a key still held starts again. Returns the health added.
        /// </summary>
        public static float Step(ref ChopperRepair repair, in RepairConfig config, ref Health health, bool held, float deltaTime)
        {
            if (repair.State == RepairPhase.Locked)
            {
                repair.LockLeft = math.max(0f, repair.LockLeft - deltaTime);
                if (repair.LockLeft > 0f) return 0f;
                repair.Phase = (byte)RepairPhase.Idle;
            }

            if (!held || health.IsDead || health.Current >= health.Max || config.Seconds <= 0f)
            {
                repair.Phase = (byte)RepairPhase.Idle;
                return 0f;
            }

            repair.Phase = (byte)RepairPhase.Repairing;
            var before = health.Current;
            health.Current = math.min(health.Max, health.Current + health.Max / config.Seconds * deltaTime);
            if (health.Current >= health.Max) repair.Phase = (byte)RepairPhase.Idle;
            return health.Current - before;
        }

        /// <summary>Damage arrived. A repair in progress stops and locks; otherwise nothing changes.</summary>
        public static bool Interrupt(ref ChopperRepair repair, in RepairConfig config)
        {
            if (repair.State != RepairPhase.Repairing) return false;
            repair.Phase = (byte)RepairPhase.Locked;
            repair.LockLeft = math.max(0f, config.InterruptCooldown);
            if (repair.LockLeft <= 0f) repair.Phase = (byte)RepairPhase.Idle;
            return true;
        }
    }
}
