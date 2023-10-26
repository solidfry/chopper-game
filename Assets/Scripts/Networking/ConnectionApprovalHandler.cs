using Unity.Netcode;

namespace Networking
{
    public class ConnectionApprovalHandler : NetworkBehaviour
    {
        public static int MaxPlayers = 12;
        // private ServerSpawnManager _spawnManager;

        private void Start()
        {
            // _spawnManager = GetComponent<ServerSpawnManager>();
            AssignConnectionCallback();
        }

        public override void OnNetworkSpawn()
        {
            // if (IsClient && IsLocalPlayer)
                NetworkManager.OnClientDisconnectCallback += DisconnectPlayer;
        }
        
        public override void OnNetworkDespawn()
        {
            // if (IsClient && IsLocalPlayer)
                NetworkManager.OnClientDisconnectCallback -= DisconnectPlayer;
        }

        private void DisconnectPlayer(ulong obj)
        {
            NetworkManager.Singleton.DisconnectClient(obj);
        }

        private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            // if (_spawnManager == null)
            // {
            //     Debug.LogError("Spawn Manager not available");
            //     response.Approved = false;
            //     response.Reason = "Spawn Manager is not available";
            //     return;
            // }
            
            if(!PlayersCanJoin()) 
            {
                response.Approved = false;
                response.Reason = "Server is full";
                return;
            }

            if (PlayersCanJoin())
            {
                response.Approved = true;
                response.Reason = "Approved";
            }
          
            // var id = request.ClientNetworkId;
            // _spawnManager.GetSpawnLocation(out var spawnLocation);
            
            // if (spawnLocation != null)
            // {
            //     response.Approved = true;
            //     // response.CreatePlayerObject = true;
            //     // response.Position = spawnLocation.position;
            //     // response.Rotation = spawnLocation.rotation;
            //     Debug.Log(response.Position + " The position");
            //     response.Reason = "Testing the Approved message";
            // }
            // else
            // {
            //     response.Approved = false;
            //     response.Reason = "No spawn location available";
            // }
            
        }
            
        void AssignConnectionCallback() => NetworkManager.ConnectionApprovalCallback = ApprovalCheck;


        // <summary>
        // Checks if the number of players connected is less than the max players
        // </summary>
        bool PlayersCanJoin() => NetworkManager.Singleton.ConnectedClients.Count < MaxPlayers;
    }
}