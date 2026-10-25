using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PlayerInteraction
{
    /// <summary>
    /// Handles the raw input from the player. Is used in conjunction with the MovementController to apply movement
    /// </summary>

    [Serializable]
    public class InputController
    {

        public float thrust, roll, pitch, yaw;
        public float dash;

        #region Input Handling
        public void OnThrust(InputAction.CallbackContext context) => thrust = context.ReadValue<float>();
        public void OnPitch(InputAction.CallbackContext context) => pitch = context.ReadValue<float>();
        public void OnYaw(InputAction.CallbackContext context) => yaw = context.ReadValue<float>();
        public void OnRoll(InputAction.CallbackContext context) => roll = context.ReadValue<float>();
        public void OnDash(InputAction.CallbackContext context) => dash = context.ReadValue<float>();
        #endregion

        // public float ClampInputValues(float value) => Mathf.Clamp(value, -1f, 1f);
    }
}