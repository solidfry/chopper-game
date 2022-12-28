using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    public class PlayerManager : MonoBehaviour
    {
        private InputManager inputManager;
        private ChopperController playerMovement;

        private void Awake()
        {
            inputManager = GetComponent<InputManager>();
            playerMovement = GetComponent<ChopperController>();
        }

        private void FixedUpdate() => HandleAllMovement();

        void HandleAllMovement()
        {
            playerMovement.HandleThrust();
            playerMovement.HandleRoll();
            playerMovement.HandleYaw();
            playerMovement.HandlePitch();
        }
    }
}