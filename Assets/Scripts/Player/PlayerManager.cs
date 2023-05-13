using UnityEngine;

namespace Player
{
    public class PlayerManager : MonoBehaviour
    {
        [SerializeField] PlayerArgs playerArgs;
        
        private void FixedUpdate() => HandleAllMovement();

        void HandleAllMovement()
        {
            playerArgs.chopperController.HandleThrust();
            playerArgs.chopperController.HandleRoll();
            playerArgs.chopperController.HandleYaw();
            playerArgs.chopperController.HandlePitch();
        }
        
        public PlayerArgs GetPlayerArgs() => playerArgs;
    }
}