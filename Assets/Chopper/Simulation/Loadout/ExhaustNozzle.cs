using System;
using UnityEngine;

namespace Chopper.Simulation.Loadout
{
    /// <summary>An engine module's nozzle mouth: where boost jets, damage smoke and fire leave the engine.</summary>
    [Serializable]
    public struct ExhaustNozzle
    {
        [Tooltip("Relative to the engine socket, in the module's space (z = forward). The jet points back (-z).")]
        public Vector3 Position;
        [Tooltip("Nozzle size against the standard engine's (1). Scales the boost jet.")]
        public float Size;

        public ExhaustNozzle(Vector3 position, float size)
        {
            Position = position;
            Size = size;
        }
    }
}
