using Cinemachine;
using UI.Hud;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PlayerInteraction.Networking
{
    [RequireComponent(typeof(Rigidbody), typeof(PlayerInput))]
    public class PlayerManager : NetworkBehaviour
    {
        [SerializeField] private CinemachineVirtualCamera playerVirtualCamera;
        [SerializeField] private AudioListener playerAudioListener;
        
        [field: SerializeField] public PlayerInput PlayerInput { get; private set; }
        [field: SerializeField] public Rigidbody PlayerRigidbody { get; private set; }
        [field: SerializeField] public OutputHudValues OutputHudValues { get; private set; }
        [field: SerializeField] public InputController InputController { get; private set; }
        [field: SerializeField] public MovementController MovementController { get; private set; }
        [field: SerializeField] public PlayerCameraManager PlayerCameraManager { get; private set; }

        [SerializeField] VehicleValues physicsValues = new();

        public override void OnNetworkSpawn()
        {
            SetupAudioVisual();
            
            if (IsClient && IsOwner || IsServer && !IsLocalPlayer)
            {
                InitializeComponents();
            }
            
            if (IsClient && IsOwner || IsHost && IsLocalPlayer)
            {
                SetPlayerRbNonKinematic(true);
            }

            // if(IsClient && IsOwner)
            //     RequestSetIsKinematicServerRpc(false);
            // else if (IsServer && !IsLocalPlayer)
            //     SetIsKinematicClientRpc(false);
            // else if (IsServer && IsLocalPlayer)
            //     SetPlayerRbNonKinematic(true);
            
        }

      

        private void FixedUpdate()
        {
            HandleMovement();
        }

        private void Update()
        {
            if (IsClient && IsOwner || IsServer)
            {
                MovementController.OnUpdate(PlayerRigidbody.rotation, PlayerRigidbody.position);
            }
        }

        private void InitializeComponents()
        {
            if (InputController is null)
                InputController = new InputController();
            
            if (PlayerRigidbody is null)
                PlayerRigidbody = GetComponent<Rigidbody>();
            
            
            if (OutputHudValues is null)
            {
                Debug.Log("OutputHudValues was Null");
                OutputHudValues = GetComponent<OutputHudValues>();
                OutputHudValues.Initialise();
            }
            else
            {
                OutputHudValues.Initialise();
            }
            
            
          
            // TODO: Issue here with the Stabiliser because this is a new MovementController, i think it's resetting the values in the Stabiliser
            MovementController.OnStart(PlayerRigidbody, PlayerRigidbody.rotation, PlayerRigidbody.position, physicsValues);
        }

        private void HandleMovement()
        {
            if (!IsOwner) return;

            // Server Reconciliation and Authorization
            if (IsServer && IsLocalPlayer)
            {
               HandleAllMovement(InputController.thrust, InputController.yaw, InputController.pitch, InputController.roll);
            } 
            else if (IsClient && IsLocalPlayer)
            {
                // TODO: This used to be the ServerRpc, but it was causing issues with the Server Reconciliation and Authorization so
                // TODO: for now ill leave it like this until i can figure out a better way
                HandleAllMovement(InputController.thrust, InputController.yaw, InputController.pitch, InputController.roll);
            }
        }

        public void OnThrust(InputAction.CallbackContext context) => InputController.OnThrust(context);
        public void OnYaw(InputAction.CallbackContext context) => InputController.OnYaw(context);
        public void OnPitch(InputAction.CallbackContext context) => InputController.OnPitch(context);
        public void OnRoll(InputAction.CallbackContext context) => InputController.OnRoll(context);
        
        public void HandleAllMovement(float thrustInput, float yawInput, float pitchInput, float rollInput)
        {
            MovementController.HandleThrust(thrustInput);
            MovementController.HandleYaw(yawInput);
            MovementController.HandlePitch(pitchInput);
            MovementController.HandleRoll(rollInput);
        }
        
        private void SetupAudioVisual()
        {
            if (IsOwner && playerVirtualCamera is not null)
            {
                PlayerCameraManager.Initialize(playerVirtualCamera, 10);
                playerAudioListener.enabled = true;
            }
            else
                PlayerCameraManager.Initialize(playerVirtualCamera, 0);
        }
        
        void SetPlayerRbNonKinematic(bool value) => PlayerRigidbody.isKinematic = value;
        
        [ServerRpc]
        void HandleAllMovementServerRpc(float thrustInput, float yawInput, float pitchInput, float rollInput) => HandleAllMovement(thrustInput, yawInput, pitchInput, rollInput);
        
        
        [ClientRpc]
        public void SetIsKinematicClientRpc(bool value)
        {
            if (PlayerRigidbody != null)
            {
                SetPlayerRbNonKinematic(value);
            }
        }

        [ServerRpc]
        public void RequestSetIsKinematicServerRpc(bool value)
        {
            SetIsKinematicClientRpc(value);
        }
    }
}
