using Chopper.Simulation.Loadout;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Chopper.Simulation.Session
{
    /// <summary>Server: creates a player's chopper (a connection or a server bot). Shared by first spawn and respawn.</summary>
    public static class ChopperSpawning
    {
        /// <summary>
        /// Spawns the chassis ghost for <paramref name="requested"/> (no choice: the default chassis; no prefab for the
        /// chassis: the spawner's prefab) and sets its loadout. The
        /// server's <see cref="LoadoutApplySystem"/> then validates and applies it; an invalid loadout falls back to
        /// the chassis default. <paramref name="skinId"/> is cosmetic (<see cref="ChopperSkin"/>). The caller picks
        /// the place (<see cref="ChopperRespawnSystem"/>: the best <see cref="SpawnPoint"/>).
        /// </summary>
        public static void Spawn(EntityCommandBuffer ecb, in ChopperSpawner spawner, DynamicBuffer<ChassisPrefab> chassisPrefabs,
            Entity connection, int networkId, in LoadoutCode requested, ushort skinId, float3 position, quaternion rotation,
            bool isConnection = true)
        {
            var prefab = spawner.Prefab;
            var chassisId = requested.IsEmpty ? spawner.DefaultChassisId : requested.ChassisId;
            foreach (var entry in chassisPrefabs)
                if (entry.ChassisId == chassisId) { prefab = entry.Prefab; break; }

            var chopper = ecb.Instantiate(prefab);
            ecb.SetComponent(chopper, LocalTransform.FromPositionRotation(position, rotation));
            ecb.SetComponent(chopper, new GhostOwner { NetworkId = networkId });
            ecb.AddComponent(chopper, new ConnectionOwner { Value = connection });
            // Full clients address their chopper by ghost id (AutoCommandTarget); thin clients have no ghosts and send
            // input without one, so the server routes it through the connection's command target.
            // A server bot has no connection: its system writes the input on the server.
            if (isConnection) ecb.SetComponent(connection, new CommandTarget { targetEntity = chopper });
            if (!requested.IsEmpty) ecb.SetComponent(chopper, ChopperLoadout.From(requested));
            ecb.SetComponent(chopper, new ChopperSkin { SkinId = skinId });
            // Despawn the chopper when the connection (or the bot) goes away.
            ecb.AppendToBuffer(connection, new LinkedEntityGroup { Value = chopper });
        }

        /// <summary>
        /// The spawner's own point, with a sideways gap per player. Used when the map has no <see cref="SpawnPoint"/>
        /// or none of them can be used.
        /// </summary>
        public static float3 FallbackPosition(in ChopperSpawner spawner, int networkId)
        {
            var right = math.mul(spawner.Rotation, math.right());
            return spawner.Position + right * (spawner.Spacing * (networkId - 1));
        }
    }
}
