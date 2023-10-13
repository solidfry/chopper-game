using Unity.Netcode;
using UnityEngine;

namespace Networking
{
    public class ConnectionApprovalHandler : MonoBehaviour
    {
        public static int MaxPlayers = 12;

        NetworkManager NetworkManager => NetworkManager.Singleton ? NetworkManager.Singleton : null;
        ServerSpawnManager SpawnManager => ServerSpawnManager.Instance ? ServerSpawnManager.Instance : null;

        private void Start()
        {
            NetworkManager.ConnectionApprovalCallback = ApprovalCheck;
        }

        private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            if (SpawnManager == null)
            {
                Debug.LogError("Spawn Manager is null");
                response.Approved = false;
                response.Reason = "Spawn Manager is null";
                return;
            }

            if (PlayersCanJoin())
            {
                var id = request.ClientNetworkId;
                var tr = SpawnManager.UseSpawnLocation();


                if (tr != null)
                {
                    response.Approved = true;
                    response.CreatePlayerObject = true;
                    response.Position = tr.position;
                    response.Rotation = tr.rotation;
                    Debug.Log(response.Position + " The position");
                    response.Reason = "Testing the Approved approval message";
                }
                else
                {
                    response.Approved = false;
                    response.Reason = "No spawn location available";
                }
            }
            else
            {
                response.Approved = false;
                response.Reason = "Server is full";
            }
        }

        // <summary>
        // Checks if the number of players connected is less than the max players
        // </summary>
        bool PlayersCanJoin() => NetworkManager.ConnectedClients.Count < MaxPlayers;
    }
}