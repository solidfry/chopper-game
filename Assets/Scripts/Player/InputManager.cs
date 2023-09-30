using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    public class InputManager : NetworkBehaviour
    {
        public NetworkVariable<float> networkThrust = new ();
        public NetworkVariable<float> networkRoll = new ();
        public NetworkVariable<float> networkPitch = new ();
        public NetworkVariable<float> networkYaw = new ();

       


        #region Input Handling
        public void OnThrust(InputAction.CallbackContext context) => networkThrust.Value = context.ReadValue<float>();
        public void OnPitch(InputAction.CallbackContext context) => networkPitch.Value = context.ReadValue<float>();
        public void OnYaw(InputAction.CallbackContext context) => networkYaw.Value = context.ReadValue<float>();
        public void OnRoll (InputAction.CallbackContext context) => networkRoll.Value = context.ReadValue<float>();
        #endregion
        
    }
}