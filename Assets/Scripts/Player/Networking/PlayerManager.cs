using System;
using Unity.Netcode;
using UnityEngine;

namespace Player.Networking
{
    public class PlayerManager : NetworkBehaviour
    {
        // Canonical location for player args
        [SerializeField] PlayerArgs playerArgs;
        private Vector4 movementData;
        private Vector3 upVec;
        private Vector3 forwardVec;
        // readonly NetworkVariable<Vector3> networkPosition = new();
        // readonly NetworkVariable<Quaternion> networkRotation = new();
        // readonly NetworkVariable<Vector3> networkVelocity = new();
        // readonly NetworkVariable<Vector3> networkAngularVelocity = new();
        

        private void Start()
        {
            playerArgs = new PlayerArgs
            {
                transform = GetComponent<Transform>(),
                rigidbody = GetComponent<Rigidbody>(),
                inputManager = GetComponent<InputManager>(),
                movementController = GetComponent<MovementController>()
            };
            
            movementData = new(playerArgs.inputManager.thrust, playerArgs.inputManager.yaw, playerArgs.inputManager.pitch, playerArgs.inputManager.roll);
            upVec = playerArgs.transform.up;
            forwardVec = playerArgs.transform.forward;
        }

        // public override void OnNetworkSpawn()
        // {
        //     if(IsOwner)
        //         AssignNetworkValues();
        //     
        // }
        
        // void Update()
        // {
        //     if(IsOwner)
        //     {
        //         playerArgs.transform.position = networkPosition.Value;
        //         playerArgs.transform.rotation = networkRotation.Value;
        //         playerArgs.rigidbody.velocity = networkVelocity.Value;
        //         playerArgs.rigidbody.angularVelocity = networkAngularVelocity.Value;
        //     }
        // }
        
        private void FixedUpdate()
        {
            if(!IsOwner) return;
           
            if(IsLocalPlayer) 
                HandleAllMovement();
                
            if (IsClient && IsLocalPlayer)
            {
                HandleAllMovementServerRPC();
            }
        }
        
        // private void AssignNetworkValues()
        // {
        //     networkPosition.Value = playerArgs.transform.position;
        //     networkRotation.Value = playerArgs.transform.rotation;
        //     networkVelocity.Value = playerArgs.rigidbody.velocity;
        //     networkAngularVelocity.Value = playerArgs.rigidbody.angularVelocity;
        // }
        
        void HandleAllMovement()
        {
            var thrust = playerArgs.inputManager.thrust;
            var yaw = playerArgs.inputManager.yaw;
            var pitch = playerArgs.inputManager.pitch;
            var roll = playerArgs.inputManager.roll;
            var upwardVector = playerArgs.transform.up;
            var forwardVector = playerArgs.transform.forward;

            
            playerArgs.movementController.HandleThrust(thrust, upwardVector, forwardVector);
            playerArgs.movementController.HandleYaw(yaw);
            playerArgs.movementController.HandlePitch(pitch);
            playerArgs.movementController.HandleRoll(roll);
        }
        
        public PlayerArgs GetPlayerArgs() => playerArgs;
        
       [ServerRpc]
       private void HandleAllMovementServerRPC()
       {
           HandleAllMovement();
       }
       
       
       
       
    }
}