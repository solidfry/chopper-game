using Cinemachine;
using Events;
using Interactions;
using Interfaces;
using UI.Hud;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PlayerInteraction.Networking
{
    [RequireComponent(typeof(Rigidbody), typeof(PlayerInput))]
    public class PlayerManager : NetworkBehaviour, IPlayer
    {
        [SerializeField] private CinemachineVirtualCamera playerVirtualCamera;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private AudioListener playerAudioListener;
        
        [field: SerializeField] public PlayerInput PlayerInput { get; private set; }
        [field: SerializeField] public NetworkHealth Health { get; set; }
        [field: SerializeField] public ulong PlayerNetworkID { get; set; }
        [field: SerializeField] public Rigidbody PlayerRigidbody { get; private set; }
        [field: SerializeField] public GameObject PlayerModel { get; private set; }
        [field: SerializeField] public OutputHudValues OutputHudValues { get; private set; }
        [field: SerializeField] public InputController InputController { get; private set; }
        [field: SerializeField] public MovementController MovementController { get; private set; }
        [SerializeField] VehicleValues physicsValues = new();
        [field: SerializeField] public PlayerCameraManager PlayerCameraManager { get; private set; }
        
        [field: SerializeField] public UpdateHud UpdateHud { get; private set; }
        


        public override void OnNetworkSpawn()
        {
            SetupAudioVisual();
            
            if (IsClient && IsOwner || IsServer && !IsLocalPlayer)
            {
                InitializeComponents();
            }
            
            if (IsClient && IsOwner && IsLocalPlayer)
            {
                SetPlayerRbNonKinematic(true);
                SetPlayerNetworkID();
                SetLocalPlayerLayerByName();
            }
            
            SubscribeToPlayerEvents();
        }
        
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            UnSubscribeFromPlayerEvents();
        }
        
        private void SubscribeToPlayerEvents()
        {
            if (!IsServer) return;
            
            GameEvents.OnPlayerFreezeAllAllEvent += FreezePlayerClientRpc;
            GameEvents.OnPlayerUnFreezeAllAllEvent += UnFreezePlayerClientRpc;
            Health.PlayerDiedEvent += PlayerDiedClientRpc;
        }

        private void UnSubscribeFromPlayerEvents()
        {
            if (!IsServer) return;
            
            GameEvents.OnPlayerFreezeAllAllEvent -= FreezePlayerClientRpc;
            GameEvents.OnPlayerUnFreezeAllAllEvent -= UnFreezePlayerClientRpc;
            Health.PlayerDiedEvent -= PlayerDiedClientRpc;
        }

        private void SetPlayerNetworkID() => PlayerNetworkID = OwnerClientId;

        private void InitialiseHealth()
        {
            if(Health is null)
                Health = GetComponent<NetworkHealth>();

            Health.InitialiseHealth();
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
            
            InitialiseHealth();
            
            MovementController.OnStart(PlayerRigidbody, PlayerRigidbody.rotation, PlayerRigidbody.position, physicsValues);
        }
        
        
        private void HandleMovement()
        {
            if (!IsOwner) return;

            // Server Reconciliation and Authorization
            if (IsServer)
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

        void HandleAllMovement(float thrustInput, float yawInput, float pitchInput, float rollInput)
        {
            MovementController.HandleThrust(thrustInput);
            MovementController.HandleYaw(yawInput);
            MovementController.HandlePitch(pitchInput);
            MovementController.HandleRoll(rollInput);
        }
        
        private void SetupAudioVisual()
        {
            if (IsLocalPlayer && IsOwner && CamerasNotNull)
            {
                PlayerCameraManager.Initialize(playerCamera, playerVirtualCamera, 10);
                playerAudioListener.enabled = true;
                Cursor.visible = false;
            }
            else
            {
                playerCamera.enabled = false;
                playerAudioListener.enabled = false;
                playerVirtualCamera.enabled = false;
            }       
        }
        
        bool CamerasNotNull => playerCamera != null && playerVirtualCamera != null;
        
        void SetPlayerRbNonKinematic(bool value) => PlayerRigidbody.isKinematic = value;
        
        void SetLocalPlayerLayerByName() => gameObject.layer = LayerMask.NameToLayer("LocalPlayer");
        
        // [ServerRpc]
        // void HandleAllMovementServerRpc(float thrustInput, float yawInput, float pitchInput, float rollInput) => 
        //     HandleAllMovement(thrustInput, yawInput, pitchInput, rollInput);
        
      
        [ClientRpc]
        private void FreezePlayerClientRpc()
        {
            if(!IsOwner) return;
            
            DisablePlayer();
        }
        
        [ClientRpc]
        private void UnFreezePlayerClientRpc()
        {
            if(!IsOwner) return;

            EnablePlayer();
        }
        
        [ClientRpc]
        private void PlayerDiedClientRpc(ulong obj)
        {
            if (IsOwner && OwnerClientId == obj)
            {
                DisablePlayer();
            }
        }
        
        void DisablePlayer()
        {
            PlayerRigidbody.isKinematic = true;
            PlayerInput.actions.Disable();
        }
        
        void EnablePlayer()
        {
            PlayerRigidbody.isKinematic = false;
            PlayerInput.actions.Enable();
        }

        [ClientRpc]
        public void PositionPlayerClientRpc(Vector3 position, Quaternion rotation)
        {
            transform.position = position;
            transform.rotation = rotation;
            GameEvents.OnNotificationEvent?.Invoke("You have been respawned");
        }
        
    }
    
}
