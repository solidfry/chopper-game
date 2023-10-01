using System;
using Abilities;
using Unity.Netcode;
using UnityEngine;

namespace Player
{
    public class MovementController : NetworkBehaviour // Changed MonoBehaviour to NetworkBehaviour
    {
        [SerializeField] private InputManager inputManager; // Reference to InputManager
        [SerializeField] GameObject rotor;
        [SerializeField] float rotorForce = 1000f;
        [SerializeField] private float yawTorque = 500f;
        [SerializeField] private float pitchTorque = 1000f;
        [SerializeField] private float rollTorque = 1000f;
        [SerializeField] private float thrustForce = 2f;
        [SerializeField][Range(0, 1)] private float upwardThrustVectorOffset = 0.5f;
        [SerializeField] private Rigidbody rb;
        [SerializeField] private Transform tr;

        [SerializeField] Dash dash = new();

        private void Start()
        {
            if (!IsOwner) return;

            if (inputManager == null)
                inputManager = FindObjectOfType<InputManager>();

            if (rb == null)
                rb = GetComponent<Rigidbody>();
            
            if (tr == null)
                tr = GetComponent<Transform>();
            
            dash.OnStart(rb);
        }

        private void Update()
        {
            if (!IsOwner) return;

            if (inputManager != null)
            {
                if (IsServer && IsLocalPlayer)
                {
                    // Fetch input from network variables and pass it to Handle methods
                    HandleAllMovement(
                        inputManager.networkThrust.Value,
                        inputManager.networkYaw.Value,
                        inputManager.networkPitch.Value,
                        inputManager.networkRoll.Value);
                }

                if (IsClient && IsLocalPlayer)
                {
                    // Fetch input from network variables and pass it to Handle methods
                    HandleAllMovementServerRPC(
                        inputManager.networkThrust.Value,
                        inputManager.networkYaw.Value,
                        inputManager.networkPitch.Value,
                        inputManager.networkRoll.Value);
                }
            }
            

            dash.OnUpdate();
            rotor.transform.RotateAround(rotor.transform.position, rotor.transform.up, rotorForce * Time.deltaTime);
        }

        public void HandleThrust(float thrustInput)
        {
            Vector3 up = tr.up;
            Vector3 forward = tr.forward;
            Vector3 thrustVector = up + forward * upwardThrustVectorOffset;

            if (thrustInput > 0.1f)
            {
                rb.AddForce(thrustVector * (thrustForce * thrustInput));
            }
            else if (thrustInput < -0.1f)
            {
                rb.AddForce(Vector3.up * (thrustForce * thrustInput));
            }
        }

        public void HandleYaw(float yawInput)
        {
            Vector3 yawAxis = new Vector3(0, yawInput * yawTorque, 0);
            rb.AddRelativeTorque(yawAxis);
        }

        public void HandleRoll(float rollInput)
        {
            Vector3 rollAxis = new Vector3(0, 0, -rollInput * rollTorque);
            rb.AddRelativeTorque(rollAxis);
        }

        public void HandlePitch(float pitchInput)
        {
            Vector3 pitchAxis = new Vector3(pitchInput * pitchTorque, 0, 0);
            rb.AddRelativeTorque(pitchAxis);
        }

        public void HandleDash() => dash.DoAbility();

        public void HandleAllMovement(float thrustInput, float yawInput, float pitchInput, float rollInput)
        {
            HandleThrust(thrustInput);
            HandleYaw(yawInput);
            HandlePitch(pitchInput);
            HandleRoll(rollInput);
        }
        
        [ServerRpc]
        void HandleAllMovementServerRPC(float thrustInput, float yawInput, float pitchInput, float rollInput)
        {
            HandleAllMovement(thrustInput, yawInput, pitchInput, rollInput);
        }
    }
}
