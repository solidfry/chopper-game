using Unity.Netcode;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// Manages how a player will be spawned
    /// </summary>
    class PlayerSpawnManager : NetworkBehaviour
    {
  
        public override void OnNetworkSpawn() 
        {
            NetworkManager.Singleton.ConnectionApprovalCallback = Approved;
            // NetworkManager.Singleton.StartHost();
        }

        private void Approved(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {   
            response.Position = new Vector3(Random.Range(0, 100), Random.Range(0, 100), Random.Range(0, 100));
        }
        
        // Get a position from the server spawn manager
        
        // Spawn the player at that position
        
        public void SpawnPlayer()
        {
            
        }
        
    }
}