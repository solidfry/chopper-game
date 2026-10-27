using Unity.Entities;

namespace Chopper.Simulation.Input
{
    /// <summary>
    /// Client world singleton: while <see cref="Blocked"/>, the local player's flight input is neutral (a menu is open).
    /// Presentation writes it (<c>InputBlockSystem</c>); <see cref="Flight.ChopperInputSystem"/> reads it.
    /// </summary>
    public struct PlayerInputBlock : IComponentData
    {
        public bool Blocked;
    }
}
