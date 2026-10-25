using Unity.NetCode;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace Chopper.Simulation
{
    /// <summary>
    /// Replaces the default Netcode for Entities bootstrap. Menus and the hangar run with only a local world.
    /// Gameplay scenes get a server and a client world; the client connects to the local server, so training
    /// runs single player with the same code as multiplayer.
    /// </summary>
    [Preserve]
    public sealed class ChopperBootstrap : ClientServerBootstrap
    {
        public const string TrainingRangeScene = "TrainingRange";
        public const ushort LocalPort = 7979;

        public override bool Initialize(string defaultWorldName)
        {
            // TODO: Phase 3 replaces this check with session start from the menu (Multiplayer Services).
            if (SceneManager.GetActiveScene().name == TrainingRangeScene)
            {
                AutoConnectPort = LocalPort;
                CreateDefaultClientServerWorlds();
                return true;
            }

            AutoConnectPort = 0;
            CreateLocalWorld(defaultWorldName);
            return true;
        }
    }
}
