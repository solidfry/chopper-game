using Events;
using Unity.Netcode;

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
            NetworkManager.OnClientDisconnectCallback += DisconnectPlayer;
        }
        
        public override void OnNetworkDespawn()
        {
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
  
            if(!PlayersCanJoin()) 
            {
                response.Approved = false;
                response.Reason = "Server is full";
                GameEvents.OnNotificationEvent?.Invoke("The server is full");

                return;
            }

            if (PlayersCanJoin())
            {
                response.Approved = true;
                response.Reason = "Approved";
                GameEvents.OnNotificationEvent?.Invoke("Joining Game");
            }
            
        }
            


        // <summary>
        // Checks if the number of players connected is less than the max players
        // </summary>
        bool PlayersCanJoin() => NetworkManager.Singleton.ConnectedClients.Count < MaxPlayers;
    }
}