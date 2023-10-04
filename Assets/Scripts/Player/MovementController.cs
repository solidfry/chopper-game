using System;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Takes input from the InputController and applies it to the vehicle
    /// </summary>
    [Serializable]
    public class MovementController 
    {
        private float _upwardThrustVectorOffset;
        private Rigidbody _rigidbody;
        VehicleValues _physicsValues;
        Quaternion _rotation;
        Vector3 _position;
        Vector3 _up;
        Vector3 _forward;
        [SerializeField][ReadOnly] Vector3 thrustVector;
        [SerializeField] VehicleStabiliser stabiliser;
    

        public MovementController(Rigidbody rigidbody, Quaternion rotation, Vector3 position, VehicleValues physicsValues)
        {
            _physicsValues = physicsValues;
            _rigidbody = rigidbody;
            _rotation = rotation;
            _position = position;
            _upwardThrustVectorOffset = physicsValues.thrustVectorOffset;
        }

        public void OnStart()
        {
            stabiliser.OnStart();
        }
        
        public void OnUpdate(Quaternion rotation, Vector3 position) => UpdateMovementVariables(rotation, position);

        public void HandleYaw(float yawInput)
        {
            Vector3 yawAxis = new Vector3(0, yawInput * _physicsValues.yawTorque, 0);
            _rigidbody.AddRelativeTorque(yawAxis);
        }
        
        public void HandlePitch(float pitchInput)
        {
            Vector3 pitchAxis = new Vector3(pitchInput * _physicsValues.pitchTorque, 0, 0);
            _rigidbody.AddRelativeTorque(pitchAxis);
        }

        public void HandleRoll(float rollInput)
        {
            Vector3 rollAxis = new Vector3(0, 0, -rollInput * _physicsValues.rollTorque);
            _rigidbody.AddRelativeTorque(rollAxis);
        }
        
        public void HandleThrust(float thrustInput)
        {
            if (thrustInput > 0.1f)
            {
                _rigidbody.AddForce(thrustVector * (_physicsValues.thrustForce * thrustInput));
            }
            else if (thrustInput < -0.1f)
            {
                _rigidbody.AddForce(Vector3.up * (_physicsValues.thrustForce * thrustInput));
            }
        }
        
        private void UpdateMovementVariables(Quaternion rotation, Vector3 position)
        {
            _position = position;
            _rotation = rotation;
            _up = _rotation * Vector3.up;
            _forward = _rotation * Vector3.forward;
            thrustVector = _up + _forward * _upwardThrustVectorOffset;

            stabiliser.UpdateStabiliser(_rigidbody, _up);
        }

        
    }
}
