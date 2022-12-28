using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    public class InputManager : MonoBehaviour
    {
        public float thrust;
        public float roll;
        public float pitch;
        public float yaw;

        #region Input Handling
        public void OnThrust(InputAction.CallbackContext context) => thrust = context.ReadValue<float>();
        public void OnPitch(InputAction.CallbackContext context) => pitch = context.ReadValue<float>();
        public void OnYaw(InputAction.CallbackContext context) => yaw = context.ReadValue<float>();
        public void OnRoll (InputAction.CallbackContext context) => roll = context.ReadValue<float>();
        #endregion
        
    }
}