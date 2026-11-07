using Unity.NetCode;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// Per-tick player input for one chopper. Netcode sends it to the server and replays it during prediction.
    /// Axes are in the range -1..1.
    /// </summary>
    public struct ChopperInput : IInputComponentData
    {
        public float Thrust;
        public float Yaw;
        public float Pitch;
        public float Roll;
        /// <summary>Held: boost (uses the boost tank).</summary>
        public bool Boost;
        /// <summary>Held: dive (Ctrl). The rotor drops its hover lift and pushes down at full collective.</summary>
        public bool Dive;
        /// <summary>Held: the nose gun fires at its fire rate.</summary>
        public bool Fire;
        /// <summary>Held: pylon rockets fire at their fire rate.</summary>
        public bool FireSecondary;
        /// <summary>Select the next pylon weapon.</summary>
        public InputEvent CyclePylon;
        /// <summary>Drop flares.</summary>
        public InputEvent Countermeasure;
        /// <summary>Held: field repair (R / gamepad X). Any hit stops it for a moment.</summary>
        public bool Repair;
    }
}
