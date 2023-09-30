using System;
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
        
        // readonly NetworkVariable<Vector3> networkPosition = new();
        // readonly NetworkVariable<Quaternion> networkRotation = new();
        // readonly NetworkVariable<Vector3> networkVelocity = new();
        // readonly NetworkVariable<Vector3> networkAngularVelocity = new();
        
        private void Start()
        {
            VehicleTransform = GetComponent<Transform>();
            InputManager = GetComponent<InputManager>();
            MovementController = GetComponent<MovementController>();
            Rb = GetComponent<Rigidbody>();
        }
        
        private void FixedUpdate()
        {
            if(!IsOwner) return;
           
            if(IsServer && IsLocalPlayer) 
                HandleAllMovement();
                
            if (IsClient && IsLocalPlayer)
                HandleAllMovementServerRPC();
            
        }
 
        
        void HandleAllMovement()
        {
            MovementController.HandleThrust(InputManager.networkThrust.Value);
            MovementController.HandleYaw(InputManager.networkYaw.Value);
            MovementController.HandlePitch(InputManager.networkPitch.Value);
            MovementController.HandleRoll(InputManager.networkRoll.Value);
        }
        
        [ServerRpc]
        private void HandleAllMovementServerRPC()
        {
            MovementController.HandleThrustServerRPC(InputManager.networkThrust.Value);
            MovementController.HandleYawServerRPC(InputManager.networkYaw.Value);
            MovementController.HandlePitchServerRPC(InputManager.networkPitch.Value);
            MovementController.HandleRollServerRPC(InputManager.networkRoll.Value);
        }
       
       
       
       
    }
}