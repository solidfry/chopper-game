using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Weapons.Jobs
{
    [BurstCompile]
    public struct DistanceCalculationJob : IJob
    {
        public Vector3 currentPosition;
        public Vector3 previousPosition;
        public NativeReference<float> distanceTraveled;

        public void Execute()
        {
            distanceTraveled.Value += Vector3.Distance(currentPosition, previousPosition);
        }
    }
}