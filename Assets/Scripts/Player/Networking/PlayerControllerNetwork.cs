using Unity.Netcode;
using UnityEngine;

namespace Player.Networking
{
    public class PlayerControllerNetwork : NetworkBehaviour
    {
        PlayerArgs playerArgs;
        public NetworkVariable<Vector3> networkPosition = new();

   
        public override void OnNetworkSpawn()
        {
            if(IsOwner)
            {
                playerArgs = GetComponent<PlayerManager>().GetPlayerArgs();
                Move();
            }
        }
        
        void Update()
        {
            playerArgs.transform.position = networkPosition.Value;
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
