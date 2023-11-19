using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Interactions.Jobs
{
    // [BurstCompile]
    public struct StabilisationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> currentForwards;
        public NativeArray<Vector3> correctiveTorques;
        public float strength;
        public float deltaTime;

        public void Execute(int index)
        {
            Vector3 currentForward = currentForwards[index];
            // Assuming that Vector3.forward is the world's forward vector
            Quaternion desiredRotation = Quaternion.FromToRotation(currentForward, Vector3.forward);
    
            desiredRotation.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180) angle -= 360; // Normalize angle to -180 to 180

            // Apply correction based on the Z component of the axis
            correctiveTorques[index] = new Vector3(axis.x * (angle * strength * deltaTime), 0, axis.z * (angle * strength * deltaTime));
        }
    
    }
}