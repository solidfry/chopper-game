using Chopper.Simulation.Loadout;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Chopper.Simulation.Session
{
    /// <summary>Server: creates a player's chopper. Shared by first spawn and respawn.</summary>
    public static class ChopperSpawning
    {
        /// <summary>
        /// Spawns the chassis ghost for <paramref name="requested"/> (or the default prefab) and sets its loadout. The
        /// server's <see cref="LoadoutApplySystem"/> then validates and applies it; an invalid loadout falls back to
        /// the chassis default.
        /// </summary>
        public static void Spawn(EntityCommandBuffer ecb, in ChopperSpawner spawner, DynamicBuffer<ChassisPrefab> chassisPrefabs,
            Entity connection, int networkId, in LoadoutCode requested)
        {
            var right = math.mul(spawner.Rotation, math.right());
            var position = spawner.Position + right * (spawner.Spacing * (networkId - 1));

            var prefab = spawner.Prefab;
            if (!requested.IsEmpty)
            {
                foreach (var entry in chassisPrefabs)
                    if (entry.ChassisId == requested.ChassisId) { prefab = entry.Prefab; break; }
            }

            var chopper = ecb.Instantiate(prefab);
            ecb.SetComponent(chopper, LocalTransform.FromPositionRotation(position, spawner.Rotation));
            ecb.SetComponent(chopper, new GhostOwner { NetworkId = networkId });
            ecb.AddComponent(chopper, new ConnectionOwner { Value = connection });
            if (!requested.IsEmpty) ecb.SetComponent(chopper, ChopperLoadout.From(requested));
            // Despawn the chopper when the connection goes away.
            ecb.AppendToBuffer(connection, new LinkedEntityGroup { Value = chopper });
        }
    }
}
