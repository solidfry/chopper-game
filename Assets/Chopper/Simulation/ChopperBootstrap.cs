using Unity.Entities;
using Unity.NetCode;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace Chopper.Simulation
{
    /// <summary>
    /// Replaces the default Netcode for Entities bootstrap. Menus and the hangar run with only a local world.
    /// Gameplay scenes get a server and a client world; the client connects to the local server, so training
    /// runs single player with the same code as multiplayer. A dedicated server build (<c>UNITY_SERVER</c>, Setup →
    /// "Build Linux server") makes only a server world that listens on <c>-port</c> or Edgegap's game port (default <see cref="LocalPort"/>;
    /// <see cref="Session.ServerArgs.Port"/>) and stops when idle (<see cref="Session.ServerLifecycleSystem"/>);
    /// its build has the gameplay scene first.
    /// </summary>
    [Preserve]
    public sealed class ChopperBootstrap : ClientServerBootstrap
    {
        public const string TrainingRangeScene = "TrainingRange";
        public const ushort LocalPort = 7979;

        public override bool Initialize(string defaultWorldName)
        {
#if UNITY_SERVER
            // In a player the first scene is not loaded yet here, so the scene check below cannot work.
            AutoConnectPort = Session.ServerArgs.Port(LocalPort);
            World.DefaultGameObjectInjectionWorld = CreateServerWorld("ServerWorld");
            return true;
#else
            // TODO: Phase 3 replaces this check with session start from the menu (Multiplayer Services).
            if (IsGameplaySceneOpen())
            {
                AutoConnectPort = LocalPort;
                CreateDefaultClientServerWorlds();
                return true;
            }

            AutoConnectPort = 0;
            CreateLocalWorld(defaultWorldName);
            return true;
#endif
        }

        /// <summary>
        /// Checks every loaded scene, not only the active one: while its subscene is open for editing, the subscene
        /// can be the active scene.
        /// </summary>
        private static bool IsGameplaySceneOpen()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var name = SceneManager.GetSceneAt(i).name;
                if (name == TrainingRangeScene || name.StartsWith(TrainingRangeScene + "_")) return true;
            }
            return false;
        }
    }
}
