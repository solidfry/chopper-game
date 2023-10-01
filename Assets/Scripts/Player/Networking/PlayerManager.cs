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

        private NetworkVariable<Vector3> _currentServerPosition = new (writePerm: NetworkVariableWritePermission.Server, readPerm: NetworkVariableReadPermission.Owner);
        private NetworkVariable<Quaternion> _currentServerRotation = new (writePerm: NetworkVariableWritePermission.Server, readPerm: NetworkVariableReadPermission.Owner);
        private NetworkVariable<Vector3> _currentServerVelocity = new (writePerm: NetworkVariableWritePermission.Server, readPerm: NetworkVariableReadPermission.Owner);

        // Client-side predicted position and rotation
        private Vector3 _predictedPosition;
        private Quaternion _predictedRotation;

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
            if (!IsOwner) return;

            HandleMovement();
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
            if (InputController == null) return;

            // Client-side prediction
            MovementController.HandleAllMovement(
                InputController.thrust,
                InputController.yaw,
                InputController.pitch,
                InputController.roll);

            _predictedPosition = PlayerRigidbody.position;
            _predictedRotation = PlayerRigidbody.rotation;
            
            MovementController.OnUpdate(_predictedRotation, _predictedPosition);

            // Server Reconciliation and Authorization
            if (IsServer)
            {
                _currentServerPosition.Value = PlayerRigidbody.position;
                _currentServerRotation.Value = PlayerRigidbody.rotation;
                _currentServerVelocity.Value = PlayerRigidbody.velocity; // Include velocity
            }

            if (IsClient)
            {
                ReconcilePositionAndRotation();
            }
        }

        private const float Epsilon = 0.1f; // Adjust as needed
        private const float RotationEpsilon = 0.01f; // Adjust as needed
        private const float VelocityEpsilon = 0.1f; // Adjust as needed
        
        private void ReconcilePositionAndRotation()
        {
            UpdatePositionServerRpc(_currentServerPosition.Value);
            UpdateRotationServerRpc(_currentServerRotation.Value);
        
            bool positionCloseEnough = Vector3.Distance(_currentServerPosition.Value, _predictedPosition) < Epsilon;
            bool rotationCloseEnough = Quaternion.Angle(_currentServerRotation.Value, _predictedRotation) < RotationEpsilon;
            bool velocityCloseEnough = Vector3.Distance(_currentServerVelocity.Value, PlayerRigidbody.velocity) < VelocityEpsilon; 

            if (!positionCloseEnough || !rotationCloseEnough || !velocityCloseEnough )
            {
                PlayerRigidbody.position = _currentServerPosition.Value;
                PlayerRigidbody.rotation = _currentServerRotation.Value;
                PlayerRigidbody.velocity = _currentServerVelocity.Value; // Update the velocity as well

            }
        }

        public void OnThrust(InputAction.CallbackContext context) => InputController.OnThrust(context);
        public void OnYaw(InputAction.CallbackContext context) => InputController.OnYaw(context);
        public void OnPitch(InputAction.CallbackContext context) => InputController.OnPitch(context);
        public void OnRoll(InputAction.CallbackContext context) => InputController.OnRoll(context);

        [ServerRpc]
        void UpdatePositionServerRpc(Vector3 position)
        {
            transform.position = position;
        }

        [ServerRpc]
        void UpdateRotationServerRpc(Quaternion rotation)
        {
            transform.rotation = rotation;
        }
    }
}
