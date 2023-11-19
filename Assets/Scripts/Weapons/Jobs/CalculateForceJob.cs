using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Weapons.Jobs
{
    [BurstCompile]
    public struct CalculateForceJob : IJob
    {
        public Vector3 forward;
        public float speed;
        public NativeArray<Vector3> resultForce;

        public void Execute()
        {
            resultForce[0] = forward * speed; // Calculate the force vector
        }
    }
}