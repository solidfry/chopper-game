using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// Starts a match from a menu scene (the hangar): <see cref="ChopperBootstrap"/> gives menus only a local world, so
    /// leaving a menu needs a server and a client world. Makes both, has the server listen and the client connect on
    /// the loopback, and removes the menu's local world. Load the gameplay scene after this: its subscene loads into the
    /// new worlds. Same result as starting the editor in a gameplay scene. <see cref="Join"/> makes only a client world
    /// that connects to a hosted server (the address the matchmaker gave).
    /// </summary>
    public static class LocalSession
    {
        /// <summary>False if a server or client world is already running (nothing is changed then).</summary>
        public static bool Start(ushort port = ChopperBootstrap.LocalPort)
        {
            foreach (var world in World.All)
                if (world.IsServer() || world.IsClient()) return false;

            var menuWorlds = new List<World>();
            foreach (var world in World.All)
                if ((world.Flags & WorldFlags.Game) == WorldFlags.Game) menuWorlds.Add(world);

            // Listen and connect only by the requests below. A port left from the start-up bootstrap (the editor started
            // in a gameplay scene) would make the new worlds listen and connect a second time.
            ClientServerBootstrap.AutoConnectPort = 0;
            var server = ClientServerBootstrap.CreateServerWorld("ServerWorld");
            var client = ClientServerBootstrap.CreateClientWorld("ClientWorld");
            foreach (var world in menuWorlds) world.Dispose();

            var listen = server.EntityManager.CreateEntity(typeof(NetworkStreamRequestListen));
            server.EntityManager.SetComponentData(listen, new NetworkStreamRequestListen { Endpoint = NetworkEndpoint.AnyIpv4.WithPort(port) });
            var connect = client.EntityManager.CreateEntity(typeof(NetworkStreamRequestConnect));
            client.EntityManager.SetComponentData(connect, new NetworkStreamRequestConnect { Endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(port) });

            World.DefaultGameObjectInjectionWorld = server;
            return true;
        }

        /// <summary>
        /// Online match: makes only a client world and connects it to a hosted server (Edgegap, from the matchmaker),
        /// and removes the menu's local world. Load the gameplay scene after this. False if netcode worlds already run or
        /// the address is not valid (nothing is changed then).
        /// </summary>
        public static bool Join(string address, ushort port)
        {
            foreach (var world in World.All)
                if (world.IsServer() || world.IsClient()) return false;
            if (!NetworkEndpoint.TryParse(address, port, out var endpoint)) return false;

            var menuWorlds = new List<World>();
            foreach (var world in World.All)
                if ((world.Flags & WorldFlags.Game) == WorldFlags.Game) menuWorlds.Add(world);

            ClientServerBootstrap.AutoConnectPort = 0;
            var client = ClientServerBootstrap.CreateClientWorld("ClientWorld");
            foreach (var world in menuWorlds) world.Dispose();

            var connect = client.EntityManager.CreateEntity(typeof(NetworkStreamRequestConnect));
            client.EntityManager.SetComponentData(connect, new NetworkStreamRequestConnect { Endpoint = endpoint });
            World.DefaultGameObjectInjectionWorld = client;
            return true;
        }

        /// <summary>
        /// Leaves the match: removes every server, client and thin client world and makes a menu world again (what
        /// <see cref="ChopperBootstrap"/> gives a menu scene). Load the menu scene after this.
        /// </summary>
        public static void Stop()
        {
            var netcodeWorlds = new List<World>();
            foreach (var world in World.All)
                if (world.IsServer() || world.IsClient() || world.IsThinClient()) netcodeWorlds.Add(world);
            foreach (var world in netcodeWorlds) world.Dispose();

            foreach (var world in World.All)
                if ((world.Flags & WorldFlags.Game) == WorldFlags.Game) return;
            World.DefaultGameObjectInjectionWorld = ClientServerBootstrap.CreateLocalWorld("Default World");
        }
    }
}
