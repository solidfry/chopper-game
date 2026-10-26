using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Chopper.Simulation.Visuals
{
    /// <summary>
    /// Client only: a part of the chopper that spins (main and tail rotor). Cosmetic, so it is not sent over the
    /// network; every client turns its copy. See the RotorSpinSystem in presentation.
    /// </summary>
    [GhostComponent(PrefabType = GhostPrefabType.Client)]
    [InternalBufferCapacity(2)]
    public struct RotorSpin : IBufferElementData
    {
        public Entity Rotor;
        /// <summary>Local axes, like Transform.Rotate.</summary>
        public float3 RadiansPerSecond;
    }

    /// <summary>
    /// Put on the chopper ghost prefab root. Lists the parts that spin. Chopper → Setup fills it from the old
    /// ChopperNetworked prefab (its SciFiRotation settings).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChopperVisualsAuthoring : MonoBehaviour
    {
        [Serializable]
        public struct Rotor
        {
            public Transform Part;
            [Tooltip("Degrees per second on each local axis (the old rotors used 10000).")]
            public Vector3 DegreesPerSecond;
        }

        [SerializeField] private Rotor[] rotors = Array.Empty<Rotor>();

        public Rotor[] Rotors => rotors;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetRotors(Rotor[] value) => rotors = value;

        private sealed class Baker : Baker<ChopperVisualsAuthoring>
        {
            public override void Bake(ChopperVisualsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                var buffer = AddBuffer<RotorSpin>(entity);
                foreach (var rotor in authoring.rotors)
                {
                    if (rotor.Part == null) continue;
                    buffer.Add(new RotorSpin
                    {
                        Rotor = GetEntity(rotor.Part, TransformUsageFlags.Dynamic),
                        RadiansPerSecond = math.radians((float3)rotor.DegreesPerSecond),
                    });
                }
            }
        }
    }
}
