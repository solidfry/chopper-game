using Unity.NetCode;
using UnityEngine.Scripting;

namespace Chopper.Simulation
{
    /// <summary>
    /// Replaces the default Netcode for Entities bootstrap. Menus and the hangar run with only a local world;
    /// server and client worlds are created when a match or training session starts.
    /// </summary>
    [Preserve]
    public sealed class ChopperBootstrap : ClientServerBootstrap
    {
        public override bool Initialize(string defaultWorldName)
        {
            AutoConnectPort = 0;
            CreateLocalWorld(defaultWorldName);
            return true;
        }
    }
}
