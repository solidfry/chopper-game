﻿using Unity.Netcode;

namespace Networking
{
    public class ConnectionApprovalHandler : NetworkBehaviour
    {
        public static int MaxPlayers = 12;

        private void Start()
        {
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
        
        void AssignConnectionCallback() => NetworkManager.ConnectionApprovalCallback = ApprovalCheck;
        
        private void DisconnectPlayer(ulong obj)
        {
            if(IsClient && IsLocalPlayer)
                NetworkManager.Singleton.Shutdown();
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
                // response.CreatePlayerObject = true;
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
            


        // <summary>
        // Checks if the number of players connected is less than the max players
        // </summary>
        bool PlayersCanJoin() => NetworkManager.Singleton.ConnectedClients.Count < MaxPlayers;
    }
}