using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace Player
{
    public class InputManager : NetworkBehaviour
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
            UpdateNetworkThrustServerRpc(thrust); // Use the server RPC
        }

        public void OnPitch(InputAction.CallbackContext context)
        {
            if (!IsOwner) return;

            pitch = context.ReadValue<float>();
            UpdateNetworkPitchServerRpc(pitch); // Use the server RPC
        }

        public void OnYaw(InputAction.CallbackContext context)
        {
            if (!IsOwner) return;

            yaw = context.ReadValue<float>();
            UpdateNetworkYawServerRpc(yaw); // Use the server RPC
        }

        public void OnRoll(InputAction.CallbackContext context)
        {
            if (!IsOwner) return;

            roll = context.ReadValue<float>();
            UpdateNetworkRollServerRpc(roll); // Use the server RPC
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
            }
        }

        [ServerRpc]
        public void UpdateNetworkPitchServerRpc(float value)
        {
            if (IsValidInput(value))
            {
                networkPitch.Value = value;
            }
        }

        [ServerRpc]
        public void UpdateNetworkYawServerRpc(float value)
        {
            if (IsValidInput(value))
            {
                networkYaw.Value = value;
            }
        }

        [ServerRpc]
        public void UpdateNetworkRollServerRpc(float value)
        {
            if (IsValidInput(value))
            {
                networkRoll.Value = value;
            }
        }
        #endregion
        
        private bool IsValidInput(float value) => value is >= -1 and <= 1;

    }
}
