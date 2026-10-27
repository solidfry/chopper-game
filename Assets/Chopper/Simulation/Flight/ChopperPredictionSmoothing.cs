using System;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// How the owner's predicted chopper hides server corrections. Baked from <see cref="ChopperAuthoring"/>.
    /// Ghosts without it (missiles) are not smoothed.
    /// </summary>
    public struct PredictionSmoothing : IComponentData
    {
        /// <summary>Corrections larger than this (metres) snap at once: respawn, teleport, big desync.</summary>
        public float MaxDistance;
        /// <summary>0..1: part of the position error removed per correction. 1 = snap (no smoothing).</summary>
        public float PositionFraction;
        /// <summary>0..1: part of the rotation error removed per correction.</summary>
        public float RotationFraction;

        public static PredictionSmoothing Default => new() { MaxDistance = 10f, PositionFraction = 0.25f, RotationFraction = 0.35f };

        /// <summary>
        /// The smoothing maths (pure, for the action and tests). <paramref name="previous"/> = what we predicted for this
        /// tick before the server correction; <paramref name="corrected"/> = the new prediction from the server state.
        /// Each correction removes a part of the error, so it fades out over a few ticks instead of a snap.
        /// </summary>
        public static LocalTransform Apply(in LocalTransform previous, in LocalTransform corrected, in PredictionSmoothing settings)
        {
            var error = math.distance(previous.Position, corrected.Position);
            if (error > settings.MaxDistance) return corrected;

            var result = corrected;
            result.Position = math.lerp(previous.Position, corrected.Position, math.saturate(settings.PositionFraction));
            result.Rotation = math.slerp(previous.Rotation, corrected.Rotation, math.saturate(settings.RotationFraction));
            return result;
        }
    }

    /// <summary>
    /// Netcode prediction smoothing for <see cref="LocalTransform"/>: called after a rollback with the old and the new
    /// prediction of the same tick. Physics velocity is not smoothed: it stays the server's, so the chopper flies the
    /// corrected path while its visible pose catches up.
    /// </summary>
    [BurstCompile]
    public static unsafe class ChopperSmoothingAction
    {
        public static readonly PortableFunctionPointer<GhostPredictionSmoothing.SmoothingActionDelegate> Action = new(Smooth);

        [BurstCompile(DisableDirectCall = true)]
        [AOT.MonoPInvokeCallback(typeof(GhostPredictionSmoothing.SmoothingActionDelegate))]
        private static void Smooth(IntPtr currentData, IntPtr previousData, IntPtr userData)
        {
            // Only ghosts with PredictionSmoothing (choppers); predicted missiles keep the exact server path.
            if (userData == IntPtr.Zero) return;

            ref var current = ref UnsafeUtility.AsRef<LocalTransform>((void*)currentData);
            ref var previous = ref UnsafeUtility.AsRef<LocalTransform>((void*)previousData);
            ref var settings = ref UnsafeUtility.AsRef<PredictionSmoothing>((void*)userData);
            current = PredictionSmoothing.Apply(previous, current, settings);
        }
    }

    /// <summary>Client only. Registers <see cref="ChopperSmoothingAction"/> once, then turns itself off.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct ChopperSmoothingRegistrationSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GhostPredictionSmoothing>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var smoothing = SystemAPI.GetSingletonRW<GhostPredictionSmoothing>();
            smoothing.ValueRW.RegisterSmoothingAction<LocalTransform, PredictionSmoothing>(state.EntityManager, ChopperSmoothingAction.Action);
            state.Enabled = false;
        }
    }
}
