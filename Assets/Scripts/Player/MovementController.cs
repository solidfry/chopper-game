using System;
using Abilities;
using Unity.Netcode;
using UnityEngine;

namespace Player
{
    public class MovementController : NetworkBehaviour
    {
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
            if(!IsOwner) return;
            
            if(IsLocalPlayer && IsClient)
            {
                if(rb == null)
                    rb = GetComponent<Rigidbody>();
                
                if(tr == null)
                    tr = GetComponent<Transform>();
                
                // currentSpeed = rb.velocity.magnitude;
                dash.OnStart(rb);
            }
        }

        private void Update()
        {
            if(!IsOwner) return;

            dash.OnUpdate();
            rotor.transform.RotateAround(rotor.transform.position, rotor.transform.up, rotorForce * Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if(!IsOwner) return;

            if(IsLocalPlayer && IsClient || IsLocalPlayer && IsHost)
            {
                // currentSpeed = rb.velocity.magnitude;
                // SendForwardVectorToUI?.Invoke(tr.forward);
            }
        }

        public void HandleThrust(float thrustInput)
        {
            Vector3 up = tr.up;
            Vector3 forward = tr.forward;
            
            // I want to add force in the direction of the players upward vector with a minor offset in the direction of the players forward vector
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

        // Editor only
        private void OnDrawGizmos()
        {
            Color color;
            color = Color.green;
            // local up
            DrawHelperAtCenter(this.transform.up, Color.magenta, 2f);

            color.g -= 0.5f;
            // global up
            DrawHelperAtCenter(Vector3.up, color, 1f);

            color = Color.blue;
            // local forward
            DrawHelperAtCenter(this.transform.forward, color, 2f);

            color.b -= 0.5f;
            // global forward
            DrawHelperAtCenter(Vector3.forward, color, 1f);

            color = Color.red;
            // local right
            DrawHelperAtCenter(this.transform.right, color, 2f);

            color.r -= 0.5f;
            // global right
            DrawHelperAtCenter(Vector3.right, color, 1f);
        }
        private void DrawHelperAtCenter(Vector3 direction, Color color, float scale)
        {
            Gizmos.color = color;
            Vector3 destination = transform.position + direction * scale;
            Gizmos.DrawLine(transform.position, destination);
        }
        
        
        [ServerRpc]
        public void HandleDashServerRPC()
        {
            HandleDash();
        }
        
        [ServerRpc]
        public void HandleThrustServerRPC(float thrustInput)
        {
            HandleThrust(thrustInput);
        }
        
        [ServerRpc]
        public void HandleYawServerRPC(float yawInput)
        {
            HandleYaw(yawInput);
        }
        
        [ServerRpc]
        public void HandleRollServerRPC(float rollInput)
        {
            HandleRoll(rollInput);
        }
        
        [ServerRpc]
        public void HandlePitchServerRPC(float pitchInput)
        {
            HandlePitch(pitchInput);
        }
        
        
    }
}
