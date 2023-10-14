using Unity.Netcode;
using UnityEngine;

namespace Networking
{
    public class ConnectionApprovalHandler : MonoBehaviour
    {
        public static int MaxPlayers = 12;

        NetworkManager NManager => NetworkManager.Singleton != null ? NetworkManager.Singleton : null;
        ServerSpawnManager SpawnManager => ServerSpawnManager.Instance != null ? ServerSpawnManager.Instance : null;

        public void Start()
        {
            if(NetworkManager.Singleton && NetworkManager.Singleton.IsServer)
                NManager.ConnectionApprovalCallback = ApprovalCheck;
        }

        private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            if (SpawnManager == null)
            {
                Debug.LogError("Spawn Manager not available");
                response.Approved = false;
                response.Reason = "Spawn Manager is not available";
                return;
            }
            
            if(!PlayersCanJoin()) 
            {
                response.Approved = false;
                response.Reason = "Server is full";
                return;
            }

          
            // var id = request.ClientNetworkId;
            SpawnManager.GetSpawnLocation(out var spawnLocation);
            
            if (spawnLocation != null)
            {
                response.Approved = true;
                response.CreatePlayerObject = true;
                response.Position = spawnLocation.position;
                response.Rotation = spawnLocation.rotation;
                Debug.Log(response.Position + " The position");
                response.Reason = "Testing the Approved message";
            }
            else
            {
                response.Approved = false;
                response.Reason = "No spawn location available";
            }
            
        }
        
        

        // <summary>
        // Checks if the number of players connected is less than the max players
        // </summary>
        bool PlayersCanJoin() => NManager.ConnectedClients.Count < MaxPlayers;
    }
}