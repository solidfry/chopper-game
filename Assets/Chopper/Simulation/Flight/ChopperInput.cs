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
        public InputEvent Dash;
    }
}
