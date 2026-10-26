using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Chopper.Simulation.Session
{
    /// <summary>Server: creates a player's chopper. Shared by first spawn and respawn.</summary>
    public static class ChopperSpawning
    {
        public static void Spawn(EntityCommandBuffer ecb, in ChopperSpawner spawner, Entity connection, int networkId)
        {
            var right = math.mul(spawner.Rotation, math.right());
            var position = spawner.Position + right * (spawner.Spacing * (networkId - 1));

            var chopper = ecb.Instantiate(spawner.Prefab);
            ecb.SetComponent(chopper, LocalTransform.FromPositionRotation(position, spawner.Rotation));
            ecb.SetComponent(chopper, new GhostOwner { NetworkId = networkId });
            ecb.AddComponent(chopper, new ConnectionOwner { Value = connection });
            // Despawn the chopper when the connection goes away.
            ecb.AppendToBuffer(connection, new LinkedEntityGroup { Value = chopper });
        }
    }
}
