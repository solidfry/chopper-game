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
        /// <summary>Nozzle size (1 = the standard engine's; 0 = old bake, read as 1). Scales the boost jet.</summary>
        public float Size;

        /// <summary>
        /// Baked default before a loadout is applied (<c>LoadoutApplySystem</c> then writes the fitted engine's nozzles):
        /// the rear ends of the standard engine module's twin cylinders (Kit02 Engine_Standard and
        /// Engine_Boost: +-0.28, 0, -0.45 from the socket). Owner, 20 Nov: the boost jets come from these. They face
        /// straight back. The effects bench uses the same points.
        /// </summary>
        public static float3 Offset(float side) => new(0.28f * side, 0f, -0.5f);
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

        /// <summary>A landing gear leg (kit chassis GEAR_&lt;i&gt; child, origin at its hinge). Setup fills these from the kit
        /// manifest (PartForge GearLeg).</summary>
        [Serializable]
        public struct GearLeg
        {
            public Transform Part;
            [Tooltip("Fold axis, chopper space (Quaternion.AngleAxis: +90 round +x swings a hanging leg back).")]
            public Vector3 Axis;
            public float Degrees;
            [Tooltip("Folded: also moves this far up (chopper space, m). Skids slide up instead of turning.")]
            public float Lift;
        }

        [SerializeField] private Rotor[] rotors = Array.Empty<Rotor>();
        [SerializeField] private GearLeg[] gear = Array.Empty<GearLeg>();
        [Header("Landing gear")]
        [SerializeField, Tooltip("Gear goes up above this height over the ground, m (from the chopper's centre).")]
        private float gearUpHeight = 8f;
        [SerializeField, Tooltip("Gear comes down below this height, m. Lower than the up height, so it does not flap.")]
        private float gearDownHeight = 5f;
        [SerializeField, Tooltip("Time for a full fold or unfold, s.")]
        private float gearSeconds = 1.2f;

        public Rotor[] Rotors => rotors;
        public GearLeg[] Gear => gear;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetRotors(Rotor[] value) => rotors = value;

        /// <summary>Editor only: used by the setup tool (KitChassis).</summary>
        public void SetGear(GearLeg[] value) => gear = value;

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

            /// <summary>Legs with their fold turned from chopper space into each leg's parent space (the kit model root
            /// may be turned or scaled by the import).</summary>
            private void BakeGear(ChopperVisualsAuthoring authoring, Entity entity)
            {
                var legs = 0;
                foreach (var leg in authoring.gear)
                {
                    if (leg.Part == null) continue;
                    if (legs++ == 0) AddBuffer<LandingGearLeg>(entity);
                    var root = authoring.transform;
                    var parent = leg.Part.parent;
                    DependsOn(leg.Part);
                    var axis = root.TransformDirection(leg.Axis);
                    var lift = root.TransformVector(Vector3.up * leg.Lift);
                    if (parent != null)
                    {
                        axis = parent.InverseTransformDirection(axis);
                        lift = parent.InverseTransformVector(lift);
                    }
                    AppendToBuffer(entity, new LandingGearLeg
                    {
                        Leg = GetEntity(leg.Part, TransformUsageFlags.Dynamic),
                        DownPosition = leg.Part.localPosition,
                        DownRotation = leg.Part.localRotation,
                        Axis = math.normalizesafe((float3)axis, new float3(1f, 0f, 0f)),
                        Radians = math.radians(leg.Degrees),
                        Lift = lift,
                    });
                }
                if (legs == 0) return;
                AddComponent(entity, new LandingGearState
                {
                    UpHeight = authoring.gearUpHeight,
                    DownHeight = math.min(authoring.gearDownHeight, authoring.gearUpHeight),
                    Seconds = authoring.gearSeconds,
                    Extension = 1f,
                });
            }

            public override void Bake(ChopperVisualsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                BakeExhaust(authoring, entity);
                BakeGear(authoring, entity);
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
