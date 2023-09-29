using System;
using Abilities;
using Unity.Netcode;
using UnityEngine;

namespace Player
{
    public class ChopperController : NetworkBehaviour
    {
        [SerializeField] GameObject rotor;
        [SerializeField] float rotorForce = 1000f;
        [SerializeField] private float yawTorque = 500f;
        [SerializeField] private float pitchTorque = 1000f;
        [SerializeField] private float rollTorque = 1000f;
        [SerializeField] private float thrustForce = 2f;
        [SerializeField][Range(0, 1)] private float upwardThrustVectorOffset = 0.5f;

        public Action<Vector3> forwardVector;

        Vector3 thrustVector;

        PlayerArgs playerArgs;

        [SerializeField] private float currentSpeed;

        [SerializeField] Dash dash = new();

        private void Awake()
        {
            playerArgs = GetComponent<PlayerManager>().GetPlayerArgs();
            dash.OnStart(playerArgs.transform);
        }

        private void Update()
        {
            dash.OnUpdate();
            rotor.transform.RotateAround(rotor.transform.position, rotor.transform.up, rotorForce * Time.deltaTime);
        }

        private void FixedUpdate()
        {
            currentSpeed = playerArgs.rigidbody.velocity.magnitude;
            forwardVector?.Invoke(playerArgs.transform.forward);
        }

        public void HandleThrust()
        {

            // I want to add force in the direction of the players upward vector with a minor offset in the direction of the players forward vector
            thrustVector = playerArgs.transform.up + playerArgs.transform.forward * upwardThrustVectorOffset;

            if (playerArgs.inputManager.thrust > 0.1f)
            {
                playerArgs.rigidbody.AddForce(thrustVector * (thrustForce * playerArgs.inputManager.thrust));
            }
            else if (playerArgs.inputManager.thrust < -0.1f)
            {
                playerArgs.rigidbody.AddForce(Vector3.up * (thrustForce * playerArgs.inputManager.thrust));
            }
        }

        public void HandleYaw()
        {
            Vector3 yawAxis = new Vector3(0, playerArgs.inputManager.yaw * yawTorque, 0);
            playerArgs.rigidbody.AddRelativeTorque(yawAxis);
        }


        public void HandleRoll()
        {
            Vector3 rollAxis = new Vector3(0, 0, -playerArgs.inputManager.roll * rollTorque);
            playerArgs.rigidbody.AddRelativeTorque(rollAxis);
        }

        public void HandlePitch()
        {
            Vector3 pitchAxis = new Vector3(playerArgs.inputManager.pitch * pitchTorque, 0, 0);
            playerArgs.rigidbody.AddRelativeTorque(pitchAxis);
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
    }
}
