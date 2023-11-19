using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace PlayerInteraction.Jobs
{
    [BurstCompile]
    public struct MovementVariablesJob : IJob
    {
        public Quaternion rotation;
        public float upwardThrustVectorOffset;
        public NativeArray<Vector3> result; // 0: _up, 1: _forward, 2: thrustVector

        public void Execute()
        {
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            result[0] = up;
            result[1] = forward;
            result[2] = up + forward * upwardThrustVectorOffset;
        }
    }

}