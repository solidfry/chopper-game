using System;
using Interactions.Jobs;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Utilities;

namespace PlayerInteraction
{
    /// <summary>
    /// In theory this class helps the vehicle stay upright when flying at very low speeds slightly above 1 km/h
    /// </summary>
    [Serializable]
    public class VehicleStabiliser
    {
        [SerializeField] [ReadOnly] Rigidbody rigidbody;
        [SerializeField] private bool useStabiliser = true;
        [SerializeField] private float strength = 2f;
        [SerializeField] private float speedThreshold = 20f;

        private NativeArray<Vector3> _currentForwards;
        private NativeArray<Vector3> _correctiveTorques;
        
        public bool IsActive
        {
            get => isActive;
            private set => isActive = value;
        }

        [ReadOnly]
        [SerializeField] private bool isActive;
        
        public void OnStart(Rigidbody rigidbody)
        {
            this.rigidbody = rigidbody;
            if(this.rigidbody is null) return;
            IsActive = false;
            
            _currentForwards = new NativeArray<Vector3>(1, Allocator.Persistent);
            _correctiveTorques = new NativeArray<Vector3>(1, Allocator.Persistent);
        }
        
        public void UpdateStabiliser(Vector3 currentUp)
        {
            if (!useStabiliser || rigidbody == null) return;

            float currentSpeed = Speed.MetersPerSecondToKilometersPerHour(rigidbody.velocity.magnitude);
            if (currentSpeed <= speedThreshold)
            {
                isActive = true;
            }
            else
            {
                isActive = false;
                return;
            }

            // Set the current up vector for the job
            _currentForwards[0] = currentUp;

            StabilisationJob job = new StabilisationJob
            {
                currentForwards = _currentForwards,
                correctiveTorques = _correctiveTorques,
                strength = strength,
                deltaTime = Time.deltaTime  
            };

            JobHandle handle = job.Schedule(_currentForwards.Length, 64);
            handle.Complete();

            rigidbody.AddRelativeTorque(_correctiveTorques[0], ForceMode.Acceleration);
        }
        
        public void OnDestroy()
        {
            // Dispose of the NativeArrays
            _currentForwards.Dispose();
            _correctiveTorques.Dispose();
        }
          
        
    }
}