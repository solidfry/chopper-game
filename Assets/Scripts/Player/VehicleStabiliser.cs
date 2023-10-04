using System;
using UnityEngine;
using Utilities;

namespace Player
{
    /// <summary>
    /// In theory this class helps the vehicle stay upright when flying at very low speeds slightly above 1 km/h
    /// </summary>
    [Serializable]
    public class VehicleStabiliser
    {
        [SerializeField] private bool useStabiliser = true;
        [SerializeField] private float strength = 2f;
        [SerializeField] private float speedThreshold = 20f;
        [ReadOnly] 
        [SerializeField] private bool isActive;

        private Vector3 _correctiveTorque;
        
        // [SerializeField] private bool debug = false;
        
        public void OnStart()
        {
            isActive = false;
        }
        
        public void UpdateStabiliser(Rigidbody rigidbody, Vector3 currentUp)
        {
            if (!useStabiliser) return;

            if (GetCurrentSpeed(rigidbody) <= 1)
            {
                isActive = false;
                return;
            }
            
            if (CheckIsActive(GetCurrentSpeed(rigidbody)))
            {
                DoStabilisation(rigidbody, currentUp);
            }
        }

        private void DoStabilisation(Rigidbody rigidbody, Vector3 currentUp)
        {
            // Calculate the axis and angle of the current tilt
            Quaternion tilt = Quaternion.FromToRotation(currentUp, Vector3.up);

            Vector3 axis;
            tilt.ToAngleAxis(out float angle, out axis);
            axis.Normalize();

            // If the angle crosses 180, it'll flip direction, so correct it
            if (angle > 180) angle -= 360;
            
            // Debugging (Optional)
            // if(debug)
            //     Debug.Log($"Axis: {axis}, Angle: {angle}, Torque: {correctiveTorque}");

            // Apply the corrective torque
            _correctiveTorque = CorrectiveTorque(axis, angle);
            rigidbody.AddRelativeTorque(_correctiveTorque, ForceMode.Acceleration);
        }

        private bool CheckIsActive(float currentSpeed) => isActive = currentSpeed <= speedThreshold;
        
        private float GetCurrentSpeed(Rigidbody rigidbody) => Speed.MetersPerSecondToKilometersPerHour(rigidbody.velocity.magnitude);

        private Vector3 CorrectiveTorque(Vector3 axis, float angle) => -axis * (angle * strength * Time.deltaTime);
    }
}