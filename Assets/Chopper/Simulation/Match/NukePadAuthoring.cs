using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Simulation.Match
{
    /// <summary>Where the Capture the Nuke nuke waits at round start (server reads it; step 2 spawns the nuke here).</summary>
    public struct NukePad : IComponentData
    {
        public float3 Position;
        /// <summary>Radius of the guarded area: the pad turrets stand on this ring.</summary>
        public float Radius;
    }

    /// <summary>One per map, in the gameplay subscene. Chopper → Setup (step 4) adds it and rings it with pad turrets.</summary>
    [DisallowMultipleComponent]
    public sealed class NukePadAuthoring : MonoBehaviour
    {
        [SerializeField] private float radius = 22f;

        public float Radius => radius;

        private sealed class Baker : Baker<NukePadAuthoring>
        {
            public override void Bake(NukePadAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new NukePad { Position = authoring.transform.position, Radius = authoring.radius });
            }
        }
    }
}
