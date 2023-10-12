using Unity.Netcode;
using UnityEngine;

namespace Networking
{
    public class ConnectionApprovalHandler : MonoBehaviour
    {
        public static int MaxPlayers = 12;

        private void Awake()
        {
            NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
        }

        private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            if (NetworkManager.Singleton.ConnectedClients.Count >= MaxPlayers)
            {
                var id= request.ClientNetworkId;
                var tr = ServerSpawnManager.Instance.UseSpawnLocation();
            

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
            } else
            {
                response.Approved = false;
                response.Reason = "Server is full";
            }

        }
    }
}