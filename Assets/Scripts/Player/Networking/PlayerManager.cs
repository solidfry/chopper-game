using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player.Networking
{
    [RequireComponent(typeof(Rigidbody), typeof(PlayerInput))]
    public class PlayerManager : NetworkBehaviour
    {
        [field: SerializeField] public PlayerInput PlayerInput { get; private set; }
        [field: SerializeField] public Rigidbody PlayerRigidbody { get; private set; }
        [field: SerializeField] public InputController InputController { get; private set; }
        [field: SerializeField] public MovementController MovementController { get; private set; }

        [SerializeField] VehicleValues physicsValues = new();

        private void Start()
        {
            if (IsClient || IsServer)
            {
                InitializeComponents();
            }

            if (IsLocalPlayer || (IsServer && !IsLocalPlayer))
            {
                PlayerRigidbody.isKinematic = false;
            }
        }

        private void FixedUpdate()
        {
            HandleMovement();
        }

        private void Update()
        {
            if (IsClient || IsServer)
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
          
            MovementController = new MovementController(PlayerRigidbody, PlayerRigidbody.rotation, PlayerRigidbody.position, physicsValues);
            MovementController.OnStart();
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
                HandleAllMovementServerRpc(InputController.thrust, InputController.yaw, InputController.pitch, InputController.roll);
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
        
        [ServerRpc]
        void HandleAllMovementServerRpc(float thrustInput, float yawInput, float pitchInput, float rollInput) => HandleAllMovement(thrustInput, yawInput, pitchInput, rollInput);
    }
}
