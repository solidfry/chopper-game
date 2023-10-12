using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace PlayerInteraction
{
    public class InputManagerNetworked : NetworkBehaviour
    {
        [SerializeField] float thrust, roll, pitch, yaw;
        public NetworkVariable<float> networkThrust = new (writePerm: NetworkVariableWritePermission.Server, readPerm: NetworkVariableReadPermission.Everyone);
        public NetworkVariable<float> networkRoll = new (writePerm: NetworkVariableWritePermission.Server, readPerm: NetworkVariableReadPermission.Everyone);
        public NetworkVariable<float> networkPitch = new (writePerm: NetworkVariableWritePermission.Server, readPerm: NetworkVariableReadPermission.Everyone);
        public NetworkVariable<float> networkYaw = new (writePerm: NetworkVariableWritePermission.Server, readPerm: NetworkVariableReadPermission.Everyone);

        private void Start()
        {
            if (!IsOwner) return;

            if (IsClient && IsLocalPlayer)
            {
                thrust = 0f;
                roll = 0f;
                pitch = 0f;
                yaw = 0f;

                Debug.Log($"IsClient: {IsClient}");
                UpdateNetworkThrustServerRpc(thrust);
                UpdateNetworkPitchServerRpc(pitch);
                UpdateNetworkYawServerRpc(yaw);
                UpdateNetworkRollServerRpc(roll);
            }
        }

        #region Input Handling

        public void OnThrust(InputAction.CallbackContext context)
        {
            if (!IsOwner) return;

            thrust = context.ReadValue<float>();
            // Client-side prediction
            PredictThrust(thrust);
            // Send to server for validation
            UpdateNetworkThrustServerRpc(thrust);
        }

        public void OnPitch(InputAction.CallbackContext context)
        {
            if (!IsOwner) return;

            pitch = context.ReadValue<float>();
            // Client-side prediction
            PredictPitch(pitch);
            // Send to server for validation
            UpdateNetworkPitchServerRpc(pitch);
        }

        public void OnYaw(InputAction.CallbackContext context)
        {
            if (!IsOwner) return;

            yaw = context.ReadValue<float>();
            // Client-side prediction
            PredictYaw(yaw);
            // Send to server for validation
            UpdateNetworkYawServerRpc(yaw);
        }

        public void OnRoll(InputAction.CallbackContext context)
        {
            if (!IsOwner) return;

            roll = context.ReadValue<float>();
            // Client-side prediction
            PredictRoll(roll);
            // Send to server for validation
            UpdateNetworkRollServerRpc(roll);
        }

        #endregion

        #region Client-Side Prediction

        private void PredictThrust(float value)
        {
            // Your client-side prediction logic for thrust here
        }

        private void PredictPitch(float value)
        {
            // Your client-side prediction logic for pitch here
        }

        private void PredictYaw(float value)
        {
            // Your client-side prediction logic for yaw here
        }

        private void PredictRoll(float value)
        {
            // Your client-side prediction logic for roll here
        }

        #endregion

        #region Server RPCs

        [ServerRpc]
        public void UpdateNetworkThrustServerRpc(float value)
        {
            if (IsValidInput(value))
            {
                networkThrust.Value = value;
                Debug.Log("Server updated Thrust: " + value);
                UpdateNetworkThrustClientRpc(value); // Update the client-side value
            }
        }

        [ServerRpc]
        public void UpdateNetworkPitchServerRpc(float value)
        {
            if (IsValidInput(value))
            {
                networkPitch.Value = value;
                UpdateNetworkPitchClientRpc(value); // Update the client-side value
            }
        }

        [ServerRpc]
        public void UpdateNetworkYawServerRpc(float value)
        {
            if (IsValidInput(value))
            {
                networkYaw.Value = value;
                UpdateNetworkYawClientRpc(value); // Update the client-side value
            }
        }

        [ServerRpc]
        public void UpdateNetworkRollServerRpc(float value)
        {
            if (IsValidInput(value))
            {
                networkRoll.Value = value;
                UpdateNetworkRollClientRpc(value); // Update the client-side value
            }
        }

        #endregion

        #region Client RPCs

        [ClientRpc]
        public void UpdateNetworkThrustClientRpc(float value)
        {
            if (IsOwner) return; // Skip the owner, since they already predicted it
            thrust = value;
            // You may have logic here to adjust the thrust value based on server correction
        }

        [ClientRpc]
        public void UpdateNetworkPitchClientRpc(float value)
        {
            if (IsOwner) return;
            pitch = value;
            // You may have logic here to adjust the pitch value based on server correction
        }

        [ClientRpc]
        public void UpdateNetworkYawClientRpc(float value)
        {
            if (IsOwner) return;
            yaw = value;
            // You may have logic here to adjust the yaw value based on server correction
        }

        [ClientRpc]
        public void UpdateNetworkRollClientRpc(float value)
        {
            if (IsOwner) return;
            roll = value;
            // You may have logic here to adjust the roll value based on server correction
        }

        #endregion

        private bool IsValidInput(float value) => value is >= -1 and <= 1;

    }
}
