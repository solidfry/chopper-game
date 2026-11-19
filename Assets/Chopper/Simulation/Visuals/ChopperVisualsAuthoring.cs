using System;
using System.Collections.Generic;
using Chopper.Simulation.Loadout;
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
    /// Client only: where a boost jet flame leaves the chopper. Chopper-local position and the direction the flame
    /// shoots (unit vector). Baked from the Engine sockets (see the BoostFlameSystem in presentation).
    /// </summary>
    [GhostComponent(PrefabType = GhostPrefabType.Client)]
    [InternalBufferCapacity(2)]
    public struct ExhaustPoint : IBufferElementData
    {
        public float3 LocalPosition;
        public float3 LocalDirection;

        /// <summary>
        /// One engine socket: the twin glowing nozzles of the chassis model, measured on the Kit02 gunship (nozzle
        /// faces at +-0.51, 0.72, -2.18 with the engine socket at 0, 1.1, -1.85). They face straight back. Owner, 20 Nov:
        /// the boost should look like a jet from these points. The effects bench uses the same points.
        /// </summary>
        public static float3 Offset(float side) => new(0.51f * side, -0.38f, -0.36f);
        public static float3 Direction(float side) => new(0f, 0f, -1f);
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
            /// <summary>
            /// One engine socket: two nozzles at +-0.3 m (same layout as the smoke from Setup). Several: one nozzle per
            /// socket. None: two nozzles behind the hull centre. Flames shoot along the chopper's -Z.
            /// </summary>
            private void BakeExhaust(ChopperVisualsAuthoring authoring, Entity entity)
            {
                var buffer = AddBuffer<ExhaustPoint>(entity);
                var root = authoring.transform;
                var back = new float3(0f, 0f, -1f);
                var engines = new List<Vector3>();
                foreach (var socket in GetComponentsInChildren<SocketAuthoring>())
                    if (socket.Slot == SlotType.Engine) engines.Add(root.InverseTransformPoint(socket.transform.position));
                if (engines.Count == 1)
                {
                    buffer.Add(new ExhaustPoint { LocalPosition = (float3)engines[0] + ExhaustPoint.Offset(1f), LocalDirection = ExhaustPoint.Direction(1f) });
                    buffer.Add(new ExhaustPoint { LocalPosition = (float3)engines[0] + ExhaustPoint.Offset(-1f), LocalDirection = ExhaustPoint.Direction(-1f) });
                }
                else if (engines.Count > 1)
                {
                    foreach (var engine in engines)
                        buffer.Add(new ExhaustPoint { LocalPosition = (float3)engine + new float3(0f, 0f, -0.6f), LocalDirection = back });
                }
                else
                {
                    buffer.Add(new ExhaustPoint { LocalPosition = new float3(0.3f, 0f, -1.2f), LocalDirection = back });
                    buffer.Add(new ExhaustPoint { LocalPosition = new float3(-0.3f, 0f, -1.2f), LocalDirection = back });
                }
            }

            public override void Bake(ChopperVisualsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                BakeExhaust(authoring, entity);
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
