using System;
using Abilities;
using Events;
using PlayerInteraction.Jobs;
using UI.Hud;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace PlayerInteraction
{
    /// <summary>
    /// Takes input from the InputController and applies it to the vehicle
    /// </summary>
    [Serializable]
    public class MovementController
    {
        private Rigidbody _rigidbody;
        private float _upwardThrustVectorOffset;
        VehicleValues _physicsValues;
        Quaternion _rotation;
        Vector3 _position;
        Vector3 _up;
        Vector3 _forward;

        [SerializeField][ReadOnly] Vector3 thrustVector;

        [SerializeField] float gravityToggleAltitude = 10f;
        [SerializeField][ReadOnly] float currentAltitude;

        [Header("Abilities")]
        [SerializeField] VehicleStabiliser stabiliser;
        [SerializeField] Dash dash;

        [Header("Gravity Toggle Values")]
        [Range(0, 1)]
        [SerializeField] float gravityToggleValue;
        [SerializeField][ReadOnly] float orientationDotProduct;

        private NativeArray<Vector3> results;

        public void OnStart(Rigidbody rigidbody, Quaternion rotation, Vector3 position, VehicleValues physicsValues)
        {
            _physicsValues = physicsValues;
            _rigidbody = rigidbody;
            _rotation = rotation;
            _position = position;
            _upwardThrustVectorOffset = physicsValues.thrustVectorOffset;
            stabiliser.OnStart(rigidbody);
            dash.OnStart(rigidbody);
            _rigidbody.transform.GetComponent<OutputHudValues>().OnAltitudeChanged += UpdateCurrentAltitude;
        }

        public void OnUpdate(Quaternion rotation, Vector3 position)
        {
            UpdateMovementVariables(rotation, position);
            UseGravity();
        }

        private void UseGravity() => _rigidbody.useGravity = currentAltitude < gravityToggleAltitude || CheckOrientation() < gravityToggleValue;

        private float CheckOrientation()
        {
            orientationDotProduct = Vector3.Dot(_rigidbody.transform.up, Vector3.up);
            return orientationDotProduct;
        }

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

        public void HandleDash(float dashInput)
        {
            if (dash == null) return;

            if (dashInput >= 0.1f && dash.CanDash)
            {
                dash.DoAbility();
                GameEvents.onScreenShakeEvent?.Invoke(Enums.Strength.Low, dash.Cooldown);
            }
        }

        private void UpdateMovementVariables(Quaternion rotation, Vector3 position)
        {

            // Set up NativeArray for results
            results = new NativeArray<Vector3>(3, Allocator.TempJob);

            // Create and schedule the job
            MovementVariablesJob job = new MovementVariablesJob
            {
                rotation = rotation,
                upwardThrustVectorOffset = _upwardThrustVectorOffset,
                result = results
            };

            // Schedule the job and Complete it to ensure it's finished before proceeding
            JobHandle handle = job.Schedule();
            handle.Complete();

            // Apply the results from the job
            _up = results[0];
            _forward = results[1];
            thrustVector = results[2];

            // Dispose of the NativeArray
            results.Dispose();

            // Call other updates that need the main thread
            stabiliser.UpdateStabiliser(_forward);
            dash.OnUpdate();
        }

        public VehicleStabiliser GetStabiliser() => stabiliser;

        public void OnDestroy()
        {
            stabiliser.OnDestroy();
            if (results.IsCreated)
                results.Dispose();

            _rigidbody.transform.GetComponent<OutputHudValues>().OnAltitudeChanged -= UpdateCurrentAltitude;
        }

        private void UpdateCurrentAltitude(int alt) => currentAltitude = alt;



    }
}
