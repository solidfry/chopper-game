using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Visuals
{
    /// <summary>
    /// Client only: one landing gear leg (a GEAR_&lt;i&gt; child of the kit chassis model, origin at its hinge). Cosmetic;
    /// every client folds its own copy (LandingGearSystem in presentation). Baked from <see cref="ChopperVisualsAuthoring"/>.
    /// </summary>
    [GhostComponent(PrefabType = GhostPrefabType.Client)]
    [InternalBufferCapacity(4)]
    public struct LandingGearLeg : IBufferElementData
    {
        public Entity Leg;
        /// <summary>The leg's pose when down (as baked, in its parent's space).</summary>
        public float3 DownPosition;
        public quaternion DownRotation;
        /// <summary>Retracted: turned round <see cref="Axis"/> by <see cref="Radians"/>, then moved by <see cref="Lift"/>
        /// (both in the leg's parent space; the baker converts them from chopper space).</summary>
        public float3 Axis;
        public float Radians;
        public float3 Lift;
    }

    /// <summary>Client only: the gear's height rule and where it is now (1 = down, 0 = up).</summary>
    [GhostComponent(PrefabType = GhostPrefabType.Client)]
    public struct LandingGearState : IComponentData
    {
        /// <summary>Up above this height over the ground (m, <c>ChopperFlightState.Altitude</c>).</summary>
        public float UpHeight;
        /// <summary>Down below this height. Lower than <see cref="UpHeight"/>, so it does not flap at the line.</summary>
        public float DownHeight;
        /// <summary>Time for a full fold or unfold, s.</summary>
        public float Seconds;
        public float Extension;
        public bool Up;
    }

    /// <summary>Landing gear maths shared by the presentation system and tests (owner, 24 Nov: wheels up above a height).</summary>
    public static class LandingGear
    {
        /// <summary>
        /// Up above <paramref name="upHeight"/>, down below <paramref name="downHeight"/>; between them it keeps what it
        /// was doing, so a chopper that hovers at the line does not flap its gear.
        /// </summary>
        public static bool WantsUp(bool up, float height, float upHeight, float downHeight)
        {
            if (height > upHeight) return true;
            if (height < downHeight) return false;
            return up;
        }

        /// <summary>Extension moves to 0 (up) or 1 (down) at 1 / <paramref name="seconds"/> per second.</summary>
        public static float Step(float extension, bool up, float seconds, float deltaTime)
        {
            var rate = deltaTime / math.max(seconds, 1e-3f);
            return math.saturate(extension + (up ? -rate : rate));
        }

        /// <summary>The leg's local pose at this extension (1 = down as baked, 0 = folded), eased at both ends.</summary>
        public static void Pose(in LandingGearLeg leg, float extension, out float3 position, out quaternion rotation)
        {
            var t = 1f - math.saturate(extension);
            t = t * t * (3f - 2f * t);
            var fold = quaternion.AxisAngle(math.normalizesafe(leg.Axis, new float3(1f, 0f, 0f)), leg.Radians * t);
            rotation = math.mul(fold, leg.DownRotation);
            position = leg.DownPosition + leg.Lift * t;
        }
    }
}
