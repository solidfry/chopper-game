using Unity.Netcode;
using UnityEngine;

namespace Player.Networking
{
    public class PlayerManager : NetworkBehaviour
    {
        
        public Transform VehicleTransform { get; private set; }
        public InputManager InputManager { get; private set; }
        public Rigidbody Rb { get; private set; }
        public MovementController MovementController { get; private set; }
        
        private void Start()
        {
            if (IsOwner || IsServer)
            {
                InitializeComponents();
            }
        }
        
        private void FixedUpdate()
        {
            if (!IsOwner) return;

            if (IsServer)
                HandleAllMovement(InputManager.networkThrust.Value, InputManager.networkYaw.Value, InputManager.networkPitch.Value, InputManager.networkRoll.Value);

            if (IsClient)
                HandleAllMovementServerRpc(InputManager.networkThrust.Value, InputManager.networkYaw.Value, InputManager.networkPitch.Value, InputManager.networkRoll.Value);
        }
        
        private void InitializeComponents()
        {
            VehicleTransform = GetComponent<Transform>();
            InputManager = GetComponent<InputManager>();
            MovementController = GetComponent<MovementController>();
            Rb = GetComponent<Rigidbody>();
        }
        
        //Wrap Handle all movement
        void HandleAllMovement(float thrustInput, float yawInput, float pitchInput, float rollInput)
        {
            if (MovementController is null)
            {
                Debug.Log("Is MovementController null? " + (MovementController == null));
                return;
            }
            
            MovementController.HandleAllMovement(thrustInput, yawInput, pitchInput, rollInput);
        }

        [ServerRpc]
        // Handle all movement
        void HandleAllMovementServerRpc(float thrustInput, float yawInput, float pitchInput, float rollInput)
        {
            if (MovementController is null)
            {
                
                Debug.LogError("MovementController is null in HandleAllMovementServerRPC");
                return;
            }
            MovementController.HandleAllMovement(thrustInput, yawInput, pitchInput, rollInput);
        }
    }
}