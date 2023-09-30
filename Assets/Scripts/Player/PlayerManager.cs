using Unity.Netcode;
using UnityEngine;

namespace Player
{
    public class PlayerManager : NetworkBehaviour
    {
        [SerializeField] PlayerArgs playerArgs;
        
        NetworkVariable<Vector3> networkPosition = new();
        NetworkVariable<Quaternion> networkRotation = new();
        NetworkVariable<Vector3> networkVelocity = new();
        NetworkVariable<Vector3> networkAngularVelocity = new();
        
        private void FixedUpdate() => HandleAllMovement();

        void HandleAllMovement()
        {
            if(IsOwner)
            {
                playerArgs.chopperController.HandleThrust();
                playerArgs.chopperController.HandleRoll();
                playerArgs.chopperController.HandleYaw();
                playerArgs.chopperController.HandlePitch();
            }
        }
        
        public PlayerArgs GetPlayerArgs() => playerArgs;
        
        // Temporary for testing
        
        public override void OnNetworkSpawn()
        {
            if(IsOwner)
            {
                networkPosition.Value = playerArgs.transform.position;
                networkRotation.Value = playerArgs.transform.rotation;
                networkVelocity.Value = playerArgs.rigidbody.velocity;
                networkAngularVelocity.Value = playerArgs.rigidbody.angularVelocity;
                Move();
            }
        }
        
        void Update()
        {
            playerArgs.transform.position = networkPosition.Value;
            playerArgs.transform.rotation = networkRotation.Value;
            playerArgs.rigidbody.velocity = networkVelocity.Value;
            playerArgs.rigidbody.angularVelocity = networkAngularVelocity.Value;
        }

        public void Move()
        {
            if (NetworkManager.Singleton.IsServer)
            {
                var randomPosition = new Vector3(Random.Range(-3f, 3f), 5f, Random.Range(-3f, 3f));
                playerArgs.transform.position = randomPosition;
                networkPosition.Value = randomPosition;
                Debug.Log("Server moved player");
            }
            else
            {
                SubmitPositionServerRpc();
                Debug.Log("Server moved player via request");
            }
        }

        [ServerRpc]
        void SubmitPositionServerRpc(ServerRpcParams rpcParams = default)
        {
            networkPosition.Value = new Vector3(Random.Range(-3f, 3f),5f, Random.Range(-3f, 3f));
        }
    }
}